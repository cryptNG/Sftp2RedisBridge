using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using log4net;
using log4net.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sftp2RedisBridge;
using Sftp2RedisBridge.Contracts;
using Sftp2RedisBridge.Networking;
using Sftp2RedisBridge.Pipeline;

namespace File2EventBridge
{
    public class Program
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public static IConfiguration Configuration { get; set; }

       
        public static void Main(string[] args)
        {
            initialize();
            log.Info("Initializing Worker");
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();

                    services.AddSingleton<IRedisPipelineManager, RedisPipelineManager>();

                    services.AddSingleton<IConfiguration>(Configuration);
                    services.AddSingleton<IRemoteDirectory, RemoteDirectory>();

                    services.AddSingleton<ISftpServer, SftpServer>();
                });
        }


        private static void initialize()
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddEnvironmentVariables();

            var configEnvironment = builder.Build();
            var hostName = Dns.GetHostName();



            var configFilePath = configEnvironment.GetSection("CONFIG_FILE")?.Value;

            Console.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: (BEFORE ASSIGN) " + configFilePath);
            if (!String.IsNullOrEmpty(configFilePath))
            {
                Console.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: " + configFilePath);

                builder = new ConfigurationBuilder()
                    .AddJsonFile(configFilePath, false)
                    .AddEnvironmentVariables();
            }
            else
            {

                Console.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: (BEFORE ASSIGN) EMPTY");
                log.Warn("Environment variable 'CONFIG_FILE' is not set.");
                log.Info($"Looking for appsettings.json / appsettings.docker.json / appsettings.{hostName}.json in {Directory.GetCurrentDirectory()}");
               
                if(File.Exists("appsettings.json"))
                    log.Info("Found appsettings.json");

                if (File.Exists("appsettings.docker.json"))
                    log.Info("Found appsettings.docker.json");

                if (File.Exists($"appsettings.{hostName}.json"))
                    log.Info($"Found appsettings.{hostName}.json");
                

                builder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true)
                    .AddJsonFile("appsettings.docker.json", true)
                    .AddJsonFile($"appsettings.{hostName}.json", true)
                    .AddEnvironmentVariables();
            }

           


            Configuration = builder.Build();

            var logConfigFilePath = configEnvironment.GetSection("LOG4NET_CONFIG")?.Value;

            if (!String.IsNullOrEmpty(logConfigFilePath))
            {

                Console.WriteLine("LOGGING ENVIRONMENT - LOG4NET_FILE: " + logConfigFilePath);
                initializeLogging(logConfigFilePath);
            }
            else
            {
                Console.WriteLine("LOGGING ENVIRONMENT - LOG4NET_FILE: NO VALUE");
                initializeLogging(Configuration["Log4NetConfigFile"]);
            }

            var root = (IConfigurationRoot)Configuration;
            var debugView = root.GetDebugView();
            log.Debug(debugView);
        }

        private static void initializeLogging(String configFile)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo(configFile));
            log.Info("Logging initialized");
        }
    }
}