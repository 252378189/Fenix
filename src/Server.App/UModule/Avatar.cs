using Fenix;
using Fenix.Common;
using Fenix.Common.Attributes;
using Shared.Protocol;
using System;
using Shared.DataModel;

namespace Server.UModule
{
    [ActorType(AType.SERVER)] 
    [AccessLevel(ALevel.CLIENT_AND_SERVER)]
    [RuntimeData(typeof(Account))]
    public partial class Avatar : Actor
    {
        public Client.AvatarRef Client => (Client.AvatarRef)this.client;

        public Avatar()
        {
            
        }

        public Avatar(string uid) : base(uid)
        {

        }

        public override void onLoad()
        {
            
        }

        public override void onClientEnable()
        {
            //��ͻ��˷���Ϣ��ǰ���ǣ��Ѿ�����ClientAvatarRef,����һ��Actor��ClientRef����ȫ�ֿɼ��ģ�ֻ���ڸ�host�����ϵ���
            this.Client.client_on_api_test("", 1, (code) =>
            {
                Log.Info("client_on_api_test", code);
            });
        }

        [ServerApi]
        public void ChangeName(string name, Action<ErrCode> callback)
        {
            Get<Account>().uid = name;

            callback(ErrCode.OK);
        }

        [ServerOnly]
        public void OnMatchOk()
        {

        }
    }
}
