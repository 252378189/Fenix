//Fenix, Inc.
//

using System;
using System.Net;
using System.Net.NetworkInformation;
using DotNetty.KCP;
using DotNetty.Buffers; 
using DotNetty.Common.Utilities; 
using DotNetty.Transport.Channels;
using System.Collections.Concurrent;
using Fenix;
using Fenix.Common;
using Fenix.Common.Utils; 
using Fenix.Common.Attributes;
using System.Threading;  
using Basic = Fenix.Common.Utils.Basic; 
using System.Text;
using System.Linq; 
using TimeUtil = Fenix.Common.Utils.TimeUtil;
using System.Collections.Generic;
using System.Threading.Tasks;
using MessagePack;

namespace Fenix
{
    //一个内网IP，必须
    //一个外网IP

    public partial class Host : Entity
    {
        public string Tag { get; set; }

        public IPEndPoint LocalAddress { get; set; }

        public IPEndPoint ExternalAddress { get; set; }
        
        public bool IsClientMode { get; set; }

        protected ConcurrentDictionary<ulong, Actor> actorDic = new ConcurrentDictionary<ulong, Actor>();
        
        protected Host(string name, string ip, string extIp, int port = 0, bool clientMode = false) : base()
        {
            this.IsClientMode = clientMode;

            Global.NetManager.OnConnect += OnConnect;
            Global.NetManager.OnReceive += OnReceive;
            Global.NetManager.OnClose += OnClose;
            Global.NetManager.OnException += OnExcept;
            Global.NetManager.OnHeartBeat += OnHeartBeat;

            //如果是客户端，则用本地连接做为id
            //如果是服务端，则从名称计算一个id, 方便路由查找
            if (!clientMode)
            {
                string _ip = ip;
                string _extIp = extIp;
                int _port = port;

                if (ip == "auto")
                    _ip = Basic.GetLocalIPv4(NetworkInterfaceType.Ethernet);

                if (extIp == "auto")
                    _extIp = Basic.GetLocalIPv4(NetworkInterfaceType.Ethernet);

                if (port == 0)
                    _port = Basic.GetAvailablePort(IPAddress.Parse(_ip));

                this.LocalAddress = new IPEndPoint(IPAddress.Parse(_ip), _port);
                this.ExternalAddress = new IPEndPoint(IPAddress.Parse(_extIp), port);

                string addr = LocalAddress.ToIPv4String();

                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;

                this.Id = Basic.GenID64FromName(this.UniqueName);
                this.RegisterGlobalManager(this);  
                Global.NetManager.RegisterHost(this);
            }
            else
            {
                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;
                this.Id = Basic.GenID64FromName(this.UniqueName);

                Global.NetManager.RegisterHost(this);
            }

            if (!this.IsClientMode)
            {
                Log.Info(string.Format("{0}(ID:{1}) is running at {2} as ServerMode", this.UniqueName, this.Id, LocalAddress.ToIPv4String()));
            }
            else
            {
                Log.Info(string.Format("{0}(ID:{1}) is running as ClientMode", this.UniqueName, this.Id));
            }

            this.AddRepeatedTimer(3000, 10000, () =>
            {
                Global.NetManager.PrintPeerInfo("All peers:");

                foreach(var a in this.actorDic.Values)
                {
                    Log.Info("===========Actor info", a.Id, a.UniqueName);
                }

                Log.Info("End of Print");
            }); 
        }

        public static Host Create(string name, string ip, string extIp, int port, bool clientMode)
        {
            if (Global.Host != null)
                return Global.Host;
            try
            {
                var c = new Host(name, ip, extIp, port, clientMode);
                Global.Host = c;
                return Global.Host;
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString()); 
            }
            return null;
        }

        public static Host CreateClient()//string ip, int port)
        {
            return Create(null, "", "", 0, true); 
        }

        public static Host CreateServer(string name, string ip, string extIp, int port)
        {
            return Create(name, ip, extIp, port, false);
        }

        double lastTs = 0;

        public override byte[] Pack()
        {
            return MessagePackSerializer.Serialize<Host>(this);
        }

        public sealed override void Update()
        {
            base.EntityUpdate();

            if (IsAlive == false)
                return;

            foreach (var a in this.actorDic.Keys)
                this.actorDic[a].Update();

            Global.NetManager?.Update();
        }

        public void Shutdown()
        {
            //先销毁所有的actor, netpeer
            //再销毁自己

            foreach (var a in this.actorDic.Values)
                a.Destroy();

            this.actorDic.Clear();

            Global.NetManager.Destroy();

            this.Destroy();
        }


        protected void OnReceive(NetPeer peer, IByteBuffer buffer)
        {
            if (!peer.IsActive)
                return;
            //Log.Debug(string.Format("RECV({0}) {1} {2} {3}", peer.netType, peer.ConnId, peer.RemoteAddress, StringUtil.ToHexString(buffer.ToArray())));
            if (buffer.ReadableBytes == 1)
            {
                byte opCode = buffer.ReadByte();
                //Log.Warn("RECV_PROTOCOL", opCode);

                if (opCode == (byte)OpCode.PING)
                {
                    Log.Info(string.Format("Ping({0}) {1} FROM {2}", peer.netType, peer.ConnId, peer.RemoteAddress));
                    
                    peer.Pong();

                    if (peer != null && peer.RemoteAddress != null) 
                        Global.IdManager.ReregisterHost(peer.ConnId, peer.RemoteAddress.ToIPv4String());
                    
#if !CLIENT
                    //如果peer是客户端，则代表
                    var clientActorId = Global.IdManager.GetClientActorId(peer.ConnId);
                    if (clientActorId != 0 && this.actorDic.ContainsKey(clientActorId))
                    {
                        Global.IdManager.RegisterClientActor(clientActorId, GetActor(clientActorId).UniqueName, peer.ConnId, peer.RemoteAddress.ToIPv4String());
                    }
#endif

                    Global.NetManager.OnPong(peer);
                    return;
                }
                else if(opCode == (byte)OpCode.PONG)
                {
#if CLIENT
                    Log.Info("ping>>>" + (TimeUtil.GetTimeStampMS2() - lastTs).ToString());
#endif
                    peer.lastTickTime = TimeUtil.GetTimeStampMS2();
                    Global.NetManager.OnPong(peer); 
                    return;
                }
                else if (opCode == (byte)OpCode.GOODBYE)
                {
                    //删除这个连接
                    Global.NetManager.Deregister(peer); 
                    return;
                }
            } 

            uint protoCode = buffer.ReadUnsignedIntLE();

#if !CLIENT
            if (protoCode == OpCode.REGISTER_REQ)
            {
                var hostId = (ulong)buffer.ReadLongLE();
                var nameBytes = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(nameBytes);
                var hostName = Encoding.UTF8.GetString(nameBytes);

                var context = new RpcContext(null, peer);

                this.Register(hostId, hostName, context);

                return; 
            }
#endif

            if (protoCode == OpCode.PARTIAL)
            {
                var partialId = (ulong)buffer.ReadLongLE();
                var partIndex = buffer.ReadByte();
                var totPartCount = buffer.ReadByte();
                var payload = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(payload);

                var finalBytes = Global.NetManager.AddPartialRpc(partialId, partIndex, totPartCount, payload);
                if (finalBytes != null)
                {
                    var finalBuf = Unpooled.WrappedBuffer(finalBytes);
                    var _protoCode = finalBuf.ReadUnsignedIntLE();
#if !CLIENT
                    if (_protoCode == OpCode.REGISTER_REQ)
                    {
                        ProcessRegisterProtocol(peer, _protoCode, finalBuf);
                    }
                    else
                    {
                        ProcessRpcProtocol(peer, _protoCode, finalBuf);
                    }
#else
                    ProcessRpcProtocol(peer, _protoCode, finalBuf);
#endif
                }
                return;
            } 
                
            ProcessRpcProtocol(peer, protoCode, buffer); 
        }

        protected void OnClose(NetPeer peer)
        {
            Log.Info("OnClose", peer.ConnId, peer.IsRemoteClient);
#if !CLIENT
            if (peer.IsRemoteClient)
            {
                var aId = Global.IdManager.GetClientActorId(peer.ConnId);
                Log.Info(aId); 
                if (this.actorDic.TryGetValue(aId, out var a))
                    a.OnClientDisable();
            }
#endif
        }

        protected void OnExcept(NetPeer peer, Exception ex)
        {
            Log.Info("ONEXCEPT", peer.ConnId);
            Log.Error(ex);
        }

        protected void OnConnect(NetPeer peer)
        {

        }

        protected void OnHeartBeat()
        {
            if (!IsAlive)
                return;

            if (IsClientMode) //客户端无法访问全局缓存
            {
                lastTs = TimeUtil.GetTimeStampMS2(); 
            }
            else
            { 
                this.RegisterGlobalManager(this);
                var actorRemoveList = new List<ulong>();
                foreach (var kv in this.actorDic)
                    if (kv.Value.IsAlive)
                        this.RegisterGlobalManagerAsync(kv.Value);
                    else
                        actorRemoveList.Add(kv.Key);
                foreach (var aId in actorRemoveList)
                    actorDic.TryRemove(aId, out var _);
            }
#if !CLIENT
            Global.IdManager.SyncWithCacheAsync();
#endif
        }

#if !CLIENT
        void ProcessRegisterProtocol(NetPeer peer, uint protoCode, IByteBuffer buffer)
        {
            if (protoCode == OpCode.REGISTER_REQ)
            {
                var hostId = (ulong)buffer.ReadLongLE();
                var nameBytes = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(nameBytes);
                var hostName = Encoding.UTF8.GetString(nameBytes);

                var context = new RpcContext(null, peer);

                this.Register(hostId, hostName, context);

                return;
            }
        }
#endif

        void ProcessRpcProtocol(NetPeer peer, uint protoCode, IByteBuffer buffer)
        {
            var msgId = (ulong)buffer.ReadLongLE(); 
            var fromActorId = (ulong)buffer.ReadLongLE();
            var toActorId = (ulong)buffer.ReadLongLE();
            var bytes = new byte[buffer.ReadableBytes];
            buffer.ReadBytes(bytes);

            var packet = Packet.Create(msgId,
                protoCode,
                peer.ConnId,
                Global.Host.Id,
                fromActorId,
                toActorId,
                peer.netType,
                Global.TypeManager.GetMessageType(protoCode),
                bytes);

            Log.Info(string.Format("RECV2({0}): {1} {2} => {3} {4} >= {5} {6} => {7}",
                peer.netType,
                protoCode,
                packet.FromHostId,
                packet.ToHostId,
                packet.FromActorId,
                packet.ToActorId,
                peer.RemoteAddress.ToIPv4String(),
                peer.LocalAddress.ToIPv4String()));

            if (protoCode >= OpCode.CALL_ACTOR_METHOD && toActorId != 0)
            {
                this.CallActorMethod(packet);
            }
            else
            {
                this.CallMethod(packet);
            }
        }

        protected void RegisterGlobalManager(Host host)
        {
            Global.IdManager.RegisterHost(host, this.LocalAddress.ToIPv4String(), this.ExternalAddress.ToIPv4String());
        }

        protected void RegisterGlobalManagerAsync(Actor actor)
        {
            Task.Run(() =>
            {
                Global.IdManager.RegisterActor(actor, this.Id);
                Global.TypeManager.RegisterActorType(actor);
            });
        }

        protected void RegisterGlobalManager(Actor actor)
        { 
            Global.IdManager.RegisterActor(actor, this.Id);
            Global.TypeManager.RegisterActorType(actor); 
        }

        public override void CallMethod(Packet packet)
        {
            bool isCallback = rpcDic.ContainsKey(packet.Id);
            if (!isCallback)
            { 
                isCallback = Global.IdManager.GetRpcId(packet.Id) != 0;
            }

            if (isCallback)
            {
                if (!rpcDic.TryGetValue(packet.Id, out var cmd))
                {
                    var aId = Global.IdManager.GetRpcId(packet.Id);
                    this.actorDic.TryGetValue(aId, out var actor);
                    cmd = actor.GetRpc(packet.Id);
                }

                RemoveRpc(cmd.Id);
                cmd.Callback(packet.Payload);
            }
            else
            {
                var cmd = RpcCommand.Create(packet, null, this);
                cmd.Call(() => {
                    RemoveRpc(cmd.Id);
                });
            }
        }

        //调用Actor身上的方法
        protected void CallActorMethod(Packet packet)
        {
            if (packet.ToActorId == 0)
            {
                this.CallMethod(packet);
                return;
            }

            var actor = this.actorDic[packet.ToActorId];
            actor.CallMethod(packet);
        }



#if !CLIENT

        [ServerOnly]
        public void CreateActor(string typename, string name, Action<DefaultErrCode, string, ulong> callback, RpcContext __context)
        {
            if (name == "" || name == null)
            {
                callback(DefaultErrCode.ERROR, "", 0);
                return;
            }

            var actorId = Global.IdManager.GetActorId(name);
            if (this.actorDic.TryGetValue(actorId, out var a))
            {
                callback(DefaultErrCode.create_actor_already_exists, a.UniqueName, a.Id);
                return;
            }
#if !CLIENT
            var hostId = Global.IdManager.GetHostIdByActorId(actorId);
            if (hostId != 0)
            {
                //callback(DefaultErrCode.create_actor_remote_exists, name, actorId);
                //return;
                //迁移actor到本地
                var remoteHost = this.GetHost(hostId);
                remoteHost.MigrateActor(actorId, (code, actorData) =>
                {
                    var actor = CreateActorLocally(typename, actorData);
                    if (actor != null)
                        callback(DefaultErrCode.OK, actor.UniqueName, actor.Id);
                    else
                        callback(DefaultErrCode.ERROR, "", 0);
                });
                return;
            }
#endif
            a = CreateActorLocally(typename, name);

            if (a != null)
                callback(DefaultErrCode.OK, a.UniqueName, a.Id);
            else
                callback(DefaultErrCode.ERROR, "", 0);
        }

        //迁移actor
        [ServerOnly]
        public void MigrateActor(ulong actorId, Action<DefaultErrCode, byte[]> callback, RpcContext __context)
        {
            if(!this.actorDic.ContainsKey(actorId))
            {
                callback(DefaultErrCode.migrate_actor_not_exists, null);
                return;
            }

            this.actorDic.TryRemove(actorId, out var a);
            var actorData = a.Pack();
            if (a.Client != null)
            {
                var clientId = Global.IdManager.GetHostIdByActorId(actorId, isClient:true);
                if(clientId != 0)
                {
                    var peer = Global.NetManager.GetPeerById(clientId, Global.Config.ClientNetwork);
                    Global.NetManager.Deregister(peer);
                }

                //通知client端，重新连接server actor
                //ulong hostId = __context.Peer.ConnId;
                //var addr = __context.Peer.RemoteAddress.ToIPv4String();
                //var ip = addr.Split(':')[0];
                //var port = int.Parse(addr.Split(':')[1]);
                //a.Client.ReconnectServerActor(hostId,
                //    Global.IdManager.GetHostName(hostId),
                //    ip, port, a.Id, a.UniqueName, a.GetType().Name, (code) =>
                //    {
                //        if(code != DefaultErrCode.OK)
                //        {
                //            //直接把客户端销毁吧
                            
                //        }
                //    });

                /* if a client's actor is created somewhere else
                 * it means the client is kicked out by another client
                 * so the old client shall be destroyed.
                 * IN SHORT: a client actor cannot migrate with server actor
                 */
            }

            a.Destroy();
            callback(DefaultErrCode.OK, actorData);
        }

        [ServerOnly]
        //移除actor
        public void RemoveActor(ulong actorId, Action<DefaultErrCode> callback, RpcContext __context)
        {
            var a = this.actorDic[actorId];
        }

        [ServerOnly]
        public void Register(ulong hostId, string hostName, RpcContext __context)
        {
            if (__context.Peer.ConnId != hostId)
            {
                //修正一下peer的id 
                Global.NetManager.ChangePeerId(__context.Peer.ConnId, hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String()); 
            }
            else
            {
                Global.IdManager.RegisterHost(hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String(), __context.Peer.RemoteAddress.ToIPv4String());
            }
        } 

        [ServerApi]
        public void RegisterClient(ulong hostId, string hostName, Action<DefaultErrCode, HostInfo> callback, RpcContext __context)
        {
            if (__context.Peer.ConnId != hostId)
            {
                Global.NetManager.ChangePeerId(__context.Peer.ConnId, hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String());
            }

            Global.NetManager.RegisterClient(hostId, hostName, __context.Peer);

            var hostInfo = Global.IdManager.GetHostInfo(this.Id);

            callback(DefaultErrCode.OK, hostInfo);
        }

        [ServerApi]
        public void BindClientActor(string actorName, Action<DefaultErrCode> callback, RpcContext __context)
        {
            //首先这个actor一定是本地的
            //如果actor不在本地，则把请求转到目标host上去
            //TODO，等想到了应用场景再加

            //find actor.server
            var actorId = Global.IdManager.GetActorId(actorName);
            //var hostAddr = Global.IdManager.GetHostAddrByActorId(actorId, false);
            Global.IdManager.RegisterClientActor(actorId, actorName, __context.Packet.FromHostId, __context.Peer.RemoteAddress.ToIPv4String());
             
            //give actor.server hostId, ipaddr to client
            callback(DefaultErrCode.OK);

            //Set actor.server's client property
            var a = Global.Host.GetActor(actorId);
            a.OnClientEnable();
        }
#endif

        [ClientApi]
        public void ReconnectServerActor(ulong hostId, string hostName, string hostIP, int hostPort, 
            ulong actorId, string actorName, string aTypeName,
            Action<DefaultErrCode> callback, RpcContext __context)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(hostIP), hostPort);
            string hostAddr = ep.ToIPv4String();

            Global.IdManager.RegisterHost(hostId, hostName, hostAddr, hostAddr);
            Global.IdManager.RegisterActor(actorId, actorName, aTypeName, hostId);
             
            var avatarHost = GetHost(hostName, hostIP, hostPort);
            Global.NetManager.PrintPeerInfo("# Master.App: hostref created");
            avatarHost.BindClientActor(actorName, (code3) =>
            {
                Global.NetManager.PrintPeerInfo("# Master.App: BindClientActor called");
                Log.Info("Avatar已经重新和服务端绑定");
            });
        }
    }
}
