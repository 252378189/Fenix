 
using Fenix.Common;
using Fenix.Common.Message;
using Fenix.Common.Rpc;
using Fenix.Common.Utils;
using Fenix.Config;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Fenix
{
    public partial class ActorRef
    {
        public uint FromHostId => fromHost.Id;

        public Host fromHost;

        public Actor fromActor;

        public uint toHostId;

        public uint toActorId;

        public IPEndPoint toAddr;

        public bool isClient;

        public NetworkType NetType => ((isClient || Global.Host.IsClientMode)? RuntimeConfig.ClientNetwork : NetworkType.TCP);

        public static ActorRef Create(uint toHostId, uint toActorId, Type refType, Actor fromActor, Host fromHost, bool isClient, IPEndPoint toPeerEP=null)
        {

            //Ҫ���һ��fromActor.HostId��fromHost.Id�ǲ������
            if(fromActor!=null && fromActor.HostId != fromHost.Id)
            {
                Log.Error(string.Format("actor_and_host_id_unmatch {0} {1}", fromActor.UniqueName, fromHost.UniqueName));
                return null;
            }
            //uint toActorId = Basic.GenID32FromName(toActorName);
            //var refType = Global.TypeManager.GetRefType(toActorTypeName);

            IPEndPoint toAddr = null;
            if (toPeerEP != null)
                toAddr = toPeerEP;
            else
            {
                if (toHostId != 0)
                    toAddr = Basic.ToAddress(Global.IdManager.GetHostAddr(toHostId));//, isClient));
                else if (toActorId != 0)
                    toAddr = Basic.ToAddress(Global.IdManager.GetHostAddrByActorId(toActorId, isClient));//);
            }

            var obj = (ActorRef)Activator.CreateInstance(refType);
            obj.toHostId = toHostId;
            obj.toActorId = toActorId;
            obj.fromActor = fromActor;
            obj.fromHost = fromHost;
            obj.toAddr = toAddr;
            obj.isClient = isClient;
            return obj;
        }

        public void CallRemoteMethod(uint protocolCode, IMessage msg, Action<byte[]> cb)
        {
            //���protocode��client_api������kcp
            //������tcp
            //�ݶ����

            //var netType = NetworkType.TCP;
            //if (isClient)
            //    netType = NetworkType.KCP;

            //var api = Global.TypeManager.GetApiType(protocolCode); 
            //if (api == Common.Attributes.Api.ClientApi)
            //    netType = NetworkType.KCP;

            //if (Global.Host.IsClientMode)
            //    netType = NetworkType.KCP;

            if (fromActor != null)
                fromActor.Rpc(protocolCode, FromHostId, fromActor.Id, toHostId, this.toActorId, toAddr, this.NetType, msg, cb);
            else
                fromHost.Rpc(protocolCode, FromHostId, 0, toHostId, this.toActorId, toAddr, this.NetType, msg, cb);
        }

        public void Disconnect()
        {
            var peer = NetManager.Instance.GetPeerById(this.toHostId, this.NetType);
            NetManager.Instance.Deregister(peer);
        }
    }
}