using Fenix.Common;
using Fenix.Common.Rpc;
using Fenix.Common.Utils;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Fenix
{
    public partial class ActorRef
    {
        public uint FromHostId => fromHost.Id;

        protected Host fromHost;

        protected Actor fromActor;

        protected uint toHostId;

        protected uint toActorId;

        protected IPEndPoint toAddr;

//#if !CLIENT 
//        public static ActorRef Create(uint toActorId, Actor fromActor)
//        {
//            var toActorType = Global.TypeManager.GetActorType(toActorId);
//            var refType = Global.TypeManager.GetRefType(toActorType.Name);
//            var obj = (ActorRef)Activator.CreateInstance(refType);
//            obj.toActorId = toActorId;
//            obj.fromActor = fromActor;
//            return obj;
//        }
//#else

        public static ActorRef Create(uint toHostId, uint toActorId, Type refType, Actor fromActor, Host fromHost, IPEndPoint toPeerEP=null)
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
                if(toHostId != 0)
                    toAddr = Basic.ToAddress(Global.IdManager.GetHostAddr(toHostId));
                else if(toActorId != 0)
                    toAddr = Basic.ToAddress(Global.IdManager.GetHostAddrByActorId(toActorId));
            }

            var obj = (ActorRef)Activator.CreateInstance(refType);
            obj.toHostId = toHostId;
            obj.toActorId = toActorId;
            obj.fromActor = fromActor;
            obj.fromHost = fromHost;
            obj.toAddr = toAddr;
            return obj;
        }
//#endif
        public void CallRemoteMethod(uint protocolCode, IMessage msg, Action<byte[]> cb)
        {
            //���protocode��client_api������kcp
            //������tcp
            //�ݶ����

            var api = Global.TypeManager.GetApiType(protocolCode);
            var netType = NetworkType.TCP;
            if (api == Common.Attributes.Api.ClientApi)
                netType = NetworkType.KCP;
            if (fromActor != null)
                fromActor.Rpc(protocolCode, FromHostId, fromActor.Id, toHostId, this.toActorId, toAddr, netType, msg, cb);
            else
                fromHost.Rpc(protocolCode, FromHostId, 0, toHostId, this.toActorId, toAddr, netType, msg, cb);
        }
    }
}