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
using TimeUtil = Fenix.Common.Utils.TimeUtil;
using System.Text;
using System.Linq;

namespace Fenix
{
    //һ������IP������
    //һ������IP

    public partial class Host : Entity
    {
        //public static Host Instance = null; 

        public string Tag { get; set; } 

        protected IPEndPoint LocalAddress { get; set; }

        protected KcpHostServer kcpServer { get; set; }

        protected TcpHostServer tcpServer { get; set; } 

        public bool IsClientMode { get; set; }

        protected ConcurrentDictionary<UInt32, Actor> actorDic = new ConcurrentDictionary<UInt32, Actor>();
        
        public bool IsAlive = true;

        private Thread heartbeatTh;

        protected Host(string name, string ip, int port = 0, bool clientMode = false) : base()
        {
            this.IsClientMode = clientMode;

            //NetManager.Instance.OnConnect += (peer) => 
            NetManager.Instance.OnReceive += (peer, buffer) => OnReceiveBuffer(peer, buffer);
            NetManager.Instance.OnClose += (peer) => NetManager.Instance.Deregister(peer);
            NetManager.Instance.OnException += (peer, ex) =>
            {
                Log.Error(ex.ToString()); 
                NetManager.Instance.Deregister(peer);
            };

            NetManager.Instance.OnPeerLost += (peer) =>
            {
                if(this.actorDic.Any(m=>m.Key == peer.ConnId && m.Value.GetType().Name == "Avatar"))
                {
                    //�ͻ���û�ˣ�����ɾ��actor�ɣ����avatar)

                    this.actorDic[peer.ConnId].Destroy();
                    this.actorDic.TryRemove(peer.ConnId, out var _);
                }
            };

            //����ǿͻ��ˣ����ñ���������Ϊid
            //����Ƿ���ˣ�������Ƽ���һ��id, ����·�ɲ���
            if (!clientMode)
            {
                string _ip = ip;
                int _port = port;

                if (ip == "auto")
                    _ip = Basic.GetLocalIPv4(NetworkInterfaceType.Ethernet);

                if (port == 0)
                    _port = Basic.GetAvailablePort(IPAddress.Parse(_ip));

                this.LocalAddress = new IPEndPoint(IPAddress.Parse(_ip), _port);

                string addr = LocalAddress.ToIPv4String();

                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;

                this.Id = Basic.GenID32FromName(this.UniqueName);

                this.RegisterGlobalManager(this);

                this.SetupKcpServer();
                this.SetupTcpServer();
            }
            else
            {  
                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;
                this.Id = Basic.GenID32FromName(this.UniqueName); 
            }

            if (!this.IsClientMode)
            {
                Log.Info(string.Format("{0}(ID:{1}) is running at {2} as ServerMode", this.UniqueName, this.Id, LocalAddress.ToIPv4String()));
            }
            else
            {
                Log.Info(string.Format("{0}(ID:{1}) is running as ClientMode", this.UniqueName, this.Id));
                //Log.Info(string.Format("{0} is running at {1} as ClientMode", this.UniqueName, LocalAddress.ToIPv4String()));
            }

            heartbeatTh = new Thread(new ThreadStart(Heartbeat));
            heartbeatTh.Start();

            this.AddRepeatedTimer(3000, 3000, () =>
            {
                NetManager.Instance.PrintPeerInfo("All peers:");
            }); 
        }

        public static Host Create(string name, string ip, int port, bool clientMode)
        {
            if (Global.Host != null)
                return Global.Host;
            try
            {
                var c = new Host(name, ip, port, clientMode);
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
            return Create(null, "", 0, true); 
        }

        public static Host CreateServer(string name, string ip, int port)
        {
            return Create(name, ip, port, false);
        } 

        protected void OnReceiveBuffer(NetPeer peer, IByteBuffer buffer)
        {
            if (!peer.IsActive)
                return;
            Log.Info(string.Format("RECV({0}) {1} {2} {3}", peer.networkType, peer.ConnId, peer.RemoteAddress, StringUtil.ToHexString(buffer.ToArray())));
          
            if (buffer.ReadableBytes == 1)
            {
                byte protoCode = buffer.ReadByte();
                if (protoCode == (byte)OpCode.PING)
                {
                    Log.Info(string.Format("Ping({0}) {1} FROM {2}", peer.networkType, peer.ConnId, peer.RemoteAddress));
                    if(peer != null && peer.RemoteAddress != null) 
                        Global.IdManager.ReregisterHost(peer.ConnId, peer.RemoteAddress.ToIPv4String());
                    peer.Send(new byte[] { (byte)OpCode.PONG });
#if !CLIENT
                    //���peer�ǿͻ��ˣ������
                    var clientActorId = Global.IdManager.GetClientActorId(peer.ConnId);
                    if (clientActorId != 0 && this.actorDic.ContainsKey(clientActorId))
                    {
                        Global.IdManager.RegisterClientActor(clientActorId, GetActor(clientActorId).UniqueName, peer.ConnId, peer.RemoteAddress.ToIPv4String());
                    }
#endif
                    NetManager.Instance.OnPong(peer);
                }
                else if(protoCode == (byte)OpCode.PONG)
                {
                    NetManager.Instance.OnPong(peer);
                }
                else if (protoCode == (byte)OpCode.GOODBYE)
                {
                    //ɾ���������
                    NetManager.Instance.Deregister(peer);
                }

                return;
            }
            else
            {
                uint protoCode = buffer.ReadUnsignedIntLE();
                if (protoCode == OpCode.REGISTER_REQ)
                {
                    var hostId = buffer.ReadUnsignedIntLE();
                    var nameBytes = new byte[buffer.ReadableBytes];
                    buffer.ReadBytes(nameBytes);
                    var hostName = Encoding.UTF8.GetString(nameBytes);

                    var context = new RpcContext(null, peer);

                    this.Register(hostId, hostName, context);

                    return; 
                }

                ulong msgId = (ulong)buffer.ReadLongLE();
                //uint fromHostId = buffer.ReadUnsignedIntLE();
                uint fromActorId = buffer.ReadUnsignedIntLE();
                uint toActorId = buffer.ReadUnsignedIntLE();
                byte[] bytes = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(bytes); 
                 
                var packet = Packet.Create(msgId, 
                    protoCode, 
                    peer.ConnId, 
                    Global.Host.Id,
                    fromActorId, 
                    toActorId, 
                    peer.networkType, 
                    Global.TypeManager.GetMessageType(protoCode), 
                    bytes);

                Log.Info(string.Format("RECV2({0}): {1} {2} => {3} {4} >= {5} {6} => {7}", 
                    peer.networkType, 
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
        }

#region KCP

        protected KcpHostServer SetupKcpServer()
        {
            kcpServer = KcpHostServer.Create(this.LocalAddress);
            kcpServer.OnConnect += KcpServer_OnConnect;
            kcpServer.OnReceive += KcpServer_OnReceive;
            kcpServer.OnClose += KcpServer_OnClose;
            kcpServer.OnException += KcpServer_OnException;

            Log.Info(string.Format("KCP-Server@{0}", this.LocalAddress.ToIPv4String()));
            return kcpServer;
        }

        protected void KcpServer_OnConnect(Ukcp ukcp)
        {
            //������
            NetManager.Instance.RegisterKcp(ukcp);
            //ulong hostId = Global.IdManager.GetHostId(channel.RemoteAddress.ToIPv4String());
            Log.Info(string.Format("kcp_client_connected {0} {1}", 
                Basic.GenID32FromName(ukcp.user().Channel.Id.AsLongText()+ukcp.user().Channel.LocalAddress.ToIPv4String() + ukcp.user().RemoteAddress.ToIPv4String()), ukcp.user().RemoteAddress.ToIPv4String()));
        }

        private void KcpServer_OnReceive(Ukcp ukcp, IByteBuffer buffer)
        {
            var peer = NetManager.Instance.GetPeer(ukcp);
            OnReceiveBuffer(peer, buffer);
        }

        private void KcpServer_OnException(Ukcp ukcp, Exception ex)
        {
            Log.Error(ex.ToString());

            NetManager.Instance.DeregisterKcp(ukcp);
        }

        private void KcpServer_OnClose(Ukcp ukcp)
        {
            NetManager.Instance.DeregisterKcp(ukcp);
        }
#endregion

#region TCP
        protected TcpHostServer SetupTcpServer()
        {
            tcpServer = TcpHostServer.Create(this.LocalAddress);
            tcpServer.OnConnect += OnTcpConnect;
            tcpServer.OnReceive += OnTcpServerReceive;
            tcpServer.OnClose += OnTcpServerClose;
            tcpServer.OnException += OnTcpServerException;
            Log.Info(string.Format("TCP-Server@{0}", this.LocalAddress.ToIPv4String()));
            return tcpServer;
        }
         
        void OnTcpConnect(IChannel channel)
        {
            //������
            NetManager.Instance.RegisterChannel(channel);
            //ulong hostId = Global.IdManager.GetHostId(channel.RemoteAddress.ToIPv4String());
            Log.Info("TcpConnect: " + channel.RemoteAddress.ToIPv4String());
        }

        void OnTcpServerReceive(IChannel channel, IByteBuffer buffer)
        {
            var peer = NetManager.Instance.GetPeer(channel);
            OnReceiveBuffer(peer, buffer);
        }

        void OnTcpServerClose(IChannel channel)
        {
            NetManager.Instance.DeregisterChannel(channel);
        }

        void OnTcpServerException(IChannel channel, Exception ex)
        {
            Log.Error(ex.ToString());
            NetManager.Instance.DeregisterChannel(channel);
        } 

#endregion

        protected void Heartbeat()
        {
            while(true)
            {
                try
                {
                    //Log.Info(string.Format("Heartbeat:{0}", IsAlive));
                    NetManager.Instance?.PrintPeerInfo();
                    if (!IsAlive)
                        return;

                    if (IsClientMode) //�ͻ����޷�����ȫ�ֻ���
                    {
                        NetManager.Instance.Ping();
                    }
                    else
                    {
                        NetManager.Instance.Ping();
                        this.RegisterGlobalManager(this);
                        foreach (var kv in this.actorDic)
                            this.RegisterGlobalManager(kv.Value);
                    }
#if !CLIENT
                Global.IdManager.SyncWithCache();
#endif
                }
                catch(Exception ex)
                {
                    Log.Error(ex);
                }
                Thread.Sleep(5000);
            }
        }

        protected void Ping(NetPeer clientPeer)
        {
            clientPeer?.Send(new byte[] { (byte)OpCode.PING });
        }

        protected void RegisterGlobalManager(Host host)
        {
            Global.IdManager.RegisterHost(host, this.LocalAddress.ToIPv4String());
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

        public Actor GetActor(uint actorId)
        {
            if (this.actorDic.TryGetValue(actorId, out Actor a))
                return a;
            return null;
        }

        [ServerOnly]
        public void CreateActor(string typename, string name, Action<DefaultErrCode, string, uint> callback, RpcContext __context)
        {
            var a = CreateActor(typename, name);

            if (a != null)
                callback(DefaultErrCode.OK, a.UniqueName, a.Id);
            else
                callback(DefaultErrCode.ERROR, "", 0);
        }

        public T CreateActor<T>(string name) where T : Actor
        {
            if (name == "" || name == null)
                return null;
            var newActor = Actor.Create(typeof(T), name);
            this.ActivateActor(newActor);
            Log.Info(string.Format("CreateActor:success {0} {1}", name, newActor.Id));
            return (T)newActor;
        }

        public Actor CreateActor(string typename, string name)
        {
            if (name == "" || name == null)
                return null;

            var type = Global.TypeManager.Get(typename);
            var newActor = Actor.Create(type, name);
            Log.Info(string.Format("CreateActor:success {0} {1}", name, newActor.Id));
            ActivateActor(newActor);
            return newActor;
        }

        public Actor ActivateActor(Actor actor)
        {
            this.RegisterGlobalManager(actor);
            actor.onLoad();
            actorDic[actor.Id] = actor;
            return actor;
        }

        //Ǩ��actor
        [ServerOnly]
        public void MigrateActor(uint actorId, RpcContext __context)
        {

        }

        [ServerOnly]
        //�Ƴ�actor
        public void RemoveActor(uint actorId, RpcContext __context)
        {

        }

        [ServerApi]
        public void Register(uint hostId, string hostName, RpcContext __context)
        {
            if (__context.Peer.ConnId != hostId)
            {
                //����һ��peer��id 
                NetManager.Instance.ChangePeerId(__context.Peer.ConnId, hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String()); 
            }
            else
            {
                Global.IdManager.RegisterHost(hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String());
            }
        }

#if !CLIENT

        [ServerApi]
        public void RegisterClient(uint hostId, string hostName, Action<DefaultErrCode, HostInfo> callback, RpcContext __context)
        {
            if (__context.Peer.ConnId != hostId)
            {
                NetManager.Instance.ChangePeerId(__context.Peer.ConnId, hostId, hostName, __context.Peer.RemoteAddress.ToIPv4String());
            }

            NetManager.Instance.RegisterClient(hostId, hostName, __context.Peer);

            var hostInfo = Global.IdManager.GetHostInfo(this.Id);

            callback(DefaultErrCode.OK, hostInfo);
        }

        [ServerApi]
        public void BindClientActor(string actorName, Action<DefaultErrCode> callback, RpcContext __context)
        {
            //�������actorһ���Ǳ��ص�
            //���actor���ڱ��أ��������ת��Ŀ��host��ȥ
            //TODO�����뵽��Ӧ�ó����ټ�

            //find actor.server
            var actorId = Global.IdManager.GetActorId(actorName);
            //var hostAddr = Global.IdManager.GetHostAddrByActorId(actorId, false);
            Global.IdManager.RegisterClientActor(actorId, actorName, __context.Packet.FromHostId, __context.Peer.RemoteAddress.ToIPv4String());
             
            //give actor.server hostId, ipaddr to client
            callback(DefaultErrCode.OK);

            //Set actor.server's client property
            var a = Global.Host.GetActor(actorId);
            a.OnClientEnable(actorName);
        }
#endif
        //����Actor���ϵķ���
        protected void CallActorMethod(Packet packet)  
        {
            if(packet.ToActorId == 0)
            {
                this.CallMethod(packet);
                return;
            }

            var actor = this.actorDic[packet.ToActorId]; 
            actor.CallMethod(packet);
        } 

        public sealed override void Update()
        {
            if (IsAlive == false)
                return;

            //Log.Info(string.Format("{0}:{1}", this.GetType().Name, rpcDic.Count));

            this.CheckTimer();

            foreach (var a in this.actorDic.Keys)
            {
                this.actorDic[a].Update();
            }

            NetManager.Instance?.Update();

            //Log.Info(string.Format("C: {0}", rpcDic.Count));
        }

        public T GetService<T>(string name) where T : ActorRef
        {
            return (T)Global.GetActorRef(typeof(T), name, null, Global.Host);
        }

        public T GetAvatar<T>(string uid) where T : ActorRef
        {
            return (T)Global.GetActorRef(typeof(T), uid, null, Global.Host);
        } 
        public T GetActorRef<T>(string name) where T: ActorRef
        {
            return (T)Global.GetActorRef(typeof(T), name, null, Global.Host);
        }

        public T GetService<T>() where T : ActorRef
        {
            var refTypeName = typeof(T).Name;
            string name = refTypeName.Substring(0, refTypeName.Length - 3); 
            return (T)Global.GetActorRef(typeof(T), name, null, Global.Host);
        }

        //public T GetService<T>(string hostName, string ip, int port) where T : ActorRef
        //{
        //    var refTypeName = typeof(T).Name;
        //    string name = refTypeName.Substring(0, refTypeName.Length - 3);
        //    IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);
        //    return (T)Global.GetActorRefByAddr(typeof(T), ep, hostName, name,  null, Global.Host);
        //}

        public ActorRef GetHost(string hostName, string ip, int port)
        { 
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);
            return Global.GetActorRefByAddr(typeof(ActorRef), ep, hostName, "", null, Global.Host);
        } 

        public void Shutdown()
        {
            //���������е�actor, netpeer
            //�������Լ�

            foreach(var a in this.actorDic.Values) 
                a.Destroy();

            this.actorDic.Clear();

            NetManager.Instance.Destroy();

            IsAlive = false;

            heartbeatTh = null;
        }
    }
}
