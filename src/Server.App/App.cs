
/*
 * (c)2020 Sekkit.com
 * Fenix��һ������Actor����ģ�͵ķֲ�ʽ��Ϸ������
 * server��ͨ�Ŷ�����tcp
 * server/client֮�������tcp/kcp/websockets
 */
 
using Fenix;
using Fenix.Config;  
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration; 
using System.Text;
using Server.Config.Db;

namespace Server
{ 
    class App
    { 
        static void Main(string[] args)
        { 
            if (args.Length == 0)
            {
                var cfgList = new List<RuntimeConfig>();

                var obj = new RuntimeConfig();
                obj.ExternalIp = "auto";
                obj.InternalIp = "auto";
                obj.Port = 17777; //auto
                obj.AppName = "Login.App";
                obj.HeartbeatIntervalMS = 5000;
                obj.ClientNetwork = NetworkType.KCP;
                obj.DefaultActorNames = new List<string>()
                {
                    "LoginService"
                };

                cfgList.Add(obj);

                obj = new RuntimeConfig();
                obj.ExternalIp = "auto";
                obj.InternalIp = "auto";
                obj.Port = 17778; //auto
                obj.AppName = "Match.App";
                obj.HeartbeatIntervalMS = 5000;
                obj.ClientNetwork = NetworkType.KCP;
                obj.DefaultActorNames = new List<string>()
                {
                    "MatchService"
                };

                cfgList.Add(obj);

                obj = new RuntimeConfig();
                obj.ExternalIp = "auto";
                obj.InternalIp = "auto";
                obj.Port = 17779; //auto
                obj.AppName = "Master.App";
                obj.HeartbeatIntervalMS = 5000;
                obj.ClientNetwork = NetworkType.KCP;
                obj.DefaultActorNames = new List<string>()
                {
                    "MasterService"
                };

                cfgList.Add(obj);

                obj = new RuntimeConfig();
                obj.ExternalIp = "auto";
                obj.InternalIp = "auto";
                obj.Port = 17780; //auto
                obj.AppName = "Zone.App";
                obj.HeartbeatIntervalMS = 5000;
                obj.ClientNetwork = NetworkType.KCP;
                obj.DefaultActorNames = new List<string>()
                {
                    "ZoneService"
                };

                cfgList.Add(obj);
                 
                using (StreamWriter sw = new StreamWriter("app.json", false, Encoding.UTF8))
                {
                    var content = JsonConvert.SerializeObject(cfgList, Formatting.Indented);
                    sw.Write(content);
                }

                Environment.SetEnvironmentVariable("AppName", "Login.App");

                //Bootstrap.Start(new Assembly[] { typeof(UModule.Avatar).Assembly }, cfgList, OnInit, isMultiProcess:true); //������ģʽ

                Bootstrap.Start(new Assembly[] { typeof(UModule.Avatar).Assembly }, cfgList, OnInit, isMultiProcess: false); //������ģʽ
            }
            else
            { 
                var builder = new ConfigurationBuilder().AddCommandLine(args);
                var cmdLine = builder.Build();
                 
                //�������в��������õ����̵Ļ�������
                Environment.SetEnvironmentVariable("AppName", cmdLine["AppName"]);

                using (var sr = new StreamReader(cmdLine["Config"]))
                {
                    var cfgList = JsonConvert.DeserializeObject<List<RuntimeConfig>>(sr.ReadToEnd());
                    Bootstrap.Start(new Assembly[] { typeof(UModule.Avatar).Assembly }, cfgList, OnInit, isMultiProcess: true); //�ֲ�ʽ
                }
            }
        }

        static void OnInit()
        {
            Global.DbManager.LoadDb(DbConfig.account_db);
        }
    }
}
