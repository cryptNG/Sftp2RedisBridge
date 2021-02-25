using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Renci.SshNet;
using File2EventBridge;
using Sftp2RedisBridge.Pipeline;
using Sftp2RedisBridge.Networking;

namespace SftpHandler_Tests
{
    [TestFixture]
    public class AcceptanceTests
    {
        //[SetUp] //NOT EVERYTESTSETUP?
        public void Setup()
        {
            TestContext.WriteLine("Cleaning up files on SftpServer for TestSetup");

            IConfiguration config = setupConfig();
            SftpConfiguration sftpConfig = createSftpConfig(config);
            SftpClient client = createClient(sftpConfig);
            List<string> files = getInputDirectoryFiles(sftpConfig,client);
            if (files.Any())
            {
                TestContext.WriteLine("No files are expected in the input directory at this point");
                TestContext.WriteLine("Cleaning up");
                foreach (string file in files)
                {
                    TestContext.WriteLine("Deleting " + file);
                    client.Connect();
                    client.DeleteFile(file);
                    client.Disconnect();
                }
            }
            List<string> errorFiles = getErrorDirectoryFiles(sftpConfig, client);
            if (errorFiles.Any())
            {
                TestContext.WriteLine("No files are expected in the input directory at this point");
                TestContext.WriteLine("Cleaning up");
                foreach (string file in errorFiles)
                {
                    TestContext.WriteLine("Deleting " + file);
                    client.Connect();
                    client.DeleteFile(file);
                    client.Disconnect();
                }
            }


            TestContext.WriteLine("Cleanup finished");
        }

        private SftpClient createClient(SftpConfiguration sftpConfig)
        {
            PrivateKeyAuthenticationMethod privateKeyAuthentication = null;

            privateKeyAuthentication =
                new PrivateKeyAuthenticationMethod(
                    sftpConfig.Username, new PrivateKeyFile[] { new PrivateKeyFile(sftpConfig.PrivateKeyFile, sftpConfig.KeyFilePassword) });



            var connectionInfo =
                new ConnectionInfo(
                    sftpConfig.Host,
                    Convert.ToInt32(sftpConfig.Port),
                    sftpConfig.Username, privateKeyAuthentication);

            return new SftpClient(connectionInfo);
        }

        private SftpConfiguration createSftpConfig(IConfiguration configuration)
        {
         
            SftpConfiguration sftpConfig = new SftpConfiguration
            (configuration["SftpConnection:Auth:Username"],
               configuration["SftpConnection:Auth:PrivateKeyFile"],
               configuration["SftpConnection:Auth:KeyFilePassword"],
               configuration["SftpConnection:Host"],
               configuration["SftpConnection:Port"],
               configuration["SftpConnection:SftpPath"],
               configuration["SftpConnection:ErrorPath"]);


           return sftpConfig;

         
        }

        private IConfiguration setupConfig()
        {

            var hostName = Dns.GetHostName();
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddEnvironmentVariables();

            var configEnvironment = builder.Build();
            var configFilePath = configEnvironment.GetSection("CONFIG_FILE")?.Value;

            TestContext.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: (BEFORE ASSIGN) " + configFilePath);
            if (!String.IsNullOrEmpty(configFilePath))
            {
                TestContext.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: " + configFilePath);
                builder = new ConfigurationBuilder()
                    .AddJsonFile(configFilePath, false)
                    .AddEnvironmentVariables();
            }
            else
            {
                TestContext.WriteLine("CONFIG ENVIRONMENT - CONFIG_FILE: (BEFORE ASSIGN) EMPTY");
                builder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true)
                    .AddJsonFile("appsettings.docker.json", true)
                    .AddJsonFile($"appsettings.{hostName}.json", true)
                    .AddEnvironmentVariables();
            }
            return builder.Build();
        }

        private List<string> getInputDirectoryFiles(SftpConfiguration sftpConfig,SftpClient client)
        {
            client.Connect();
            List<string> filesInFolder = new List<string>();
            foreach (var entry in client.ListDirectory(sftpConfig.SftpPath))
            {

                TestContext.WriteLine("Directorylisting: DIRECTORY " + entry.FullName);
                if (entry.IsRegularFile)
                {
                    filesInFolder.Add(entry.FullName);
                    TestContext.WriteLine("Directorylisting: FILE " + entry.FullName);
                }
            }
            client.Disconnect();
            return filesInFolder;
        }
        private List<string> getErrorDirectoryFiles(SftpConfiguration sftpConfig, SftpClient client)
        {
            client.Connect();
            List<string> filesInFolder = new List<string>();
            foreach (var entry in client.ListDirectory(sftpConfig.ErrorPath))
            {

                TestContext.WriteLine("Directorylisting: DIRECTORY " + entry.FullName);
                if (entry.IsRegularFile)
                {
                    filesInFolder.Add(entry.FullName);
                    TestContext.WriteLine("Directorylisting: FILE " + entry.FullName);
                }
            }
            client.Disconnect();
            return filesInFolder;
        }



   

        private void createInputFile(SftpConfiguration sftpConfig,SftpClient client, string sftpFileName)
        {
            client.Connect();
            using (var stream = client.CreateText($"{sftpConfig.SftpPath}/{sftpFileName}"))
            {
                stream.WriteLine("testFileLine");
            }
            client.Disconnect();
        }

        [Test]
        public void Run_ConsumeAllToError_AllInError()
        { //TRY TO DO WITH ANNOTATION
            //TODO: BEFORE WRITING TO DONE STREAM, CHECK THAT THE KEYS ARE ALL THERE AND THE FILE IS READABLE
            Setup();

            int retries = 4;
            int delay = 1;
            int testFilesCount = 4;

            //SETUP
             IConfiguration config = setupConfig();
             SftpConfiguration sftpConfig = createSftpConfig(config);
             SftpClient client = createClient(sftpConfig);
             RedisPipelineManager redisTestPipelineManager = new RedisPipelineManager(config);
            Assert.True(redisTestPipelineManager.Initialize());

             
             TestContext.WriteLine("Asserting if input directory is empty at beginning of the test");
             List<string> inputDirectoryFilesEmpty = getInputDirectoryFiles(sftpConfig, client);
             Assert.Zero(inputDirectoryFilesEmpty.Count);
            
             createInputFile(sftpConfig,client,"acceptanceTestFile1");
             createInputFile(sftpConfig, client, "acceptanceTestFile2");
             createInputFile(sftpConfig, client, "acceptanceTestFile3");
             createInputFile(sftpConfig, client, "acceptanceTestFile4");
            
             TestContext.WriteLine("Asserting if amount of files in input dir match created amount");
             List<String> sftpInputDirFiles = getInputDirectoryFiles(sftpConfig, client);
             Assert.AreEqual(testFilesCount,sftpInputDirFiles.Count);
            //EXECUTE
             //TestContext.WriteLine("Executing SFTPHANDLER in BACKGROUND");
            // Task.Run(() => Program.Main(new string[] { }));
            
            //IF HANDLER IS RUNNING IN DOCKER
            Thread.Sleep(delay * 1000);
            List<RemoteFile> cachedFiles = new List<RemoteFile>();

            
             int i = 0;

             cachedFiles = redisTestPipelineManager.GetCachedFiles();
            while (cachedFiles.Count < testFilesCount)
            {
                 if (i == retries)
                 {
                     break;

                 }
                 cachedFiles = redisTestPipelineManager.GetCachedFiles();

                 TestContext.WriteLine($"Waiting {delay} seconds");

                Thread.Sleep(delay * 1000);
                 i++;
            } 

            
            if (!cachedFiles.Any())
            {
                TestContext.WriteLine("No data was cached within 90 seconds");
            }

            TestContext.WriteLine("Asserting if the generated files have been loaded to cache");
            Assert.AreEqual(4,cachedFiles.Count);

            foreach (RemoteFile file in cachedFiles)
            {
                TestContext.WriteLine("Writing FILE: " + file.FullName + " TUID:" + file.TransactionUid + " to ErrorStream");
                redisTestPipelineManager.WriteToErrorStream(file.TransactionUid,"acceptanceTestInducedError");
            }
             
            List<string> errorDirectoryFiles = new List<string>();

               i = 0; 
            errorDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);

            while (errorDirectoryFiles.Count < testFilesCount)
            {
                if (i == retries)
                {
                    break;
                }
                TestContext.WriteLine($"Waiting {delay} seconds");

                Thread.Sleep(delay * 1000);
                errorDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);
                i++;
            } 

            if (!errorDirectoryFiles.Any())
            {
                TestContext.WriteLine($"No data was moved to error within {delay * retries} seconds");
            }
//ASSERT
            TestContext.WriteLine("Asserting that all files have been moved to error");

            Assert.AreEqual(testFilesCount,errorDirectoryFiles.Count);

            Assert.Pass("Acceptance test criteria are met");
        }


        private byte[] createDummyErrorFileContent(string content)
        {
            
           return Encoding.ASCII.GetBytes(content);
        }
        private string makeBase64Chunky(byte[] fileData)
        {
            StringBuilder builder = new StringBuilder();
            using (var inputStream = new MemoryStream(fileData))
            {
                byte[] buffer = new byte[2187];
                int bytesRead = inputStream.Read(buffer, 0, buffer.Length);
                while (bytesRead > 0)
                {
                    String base64String = Convert.ToBase64String(buffer, 0, bytesRead);
                    builder.Append(base64String);
                    bytesRead = inputStream.Read(buffer, 0, buffer.Length);
                }
            }

            return builder.ToString();
        }

        private RemoteFile downloadFile(SftpClient client,string fileName)
        {
            client.Connect();
            try
            {
                RemoteFile remoteFile;
                using (var downloadStream = new MemoryStream())
                {
                    client.DownloadFile(fileName, downloadStream);
                    remoteFile = new RemoteFile(client.Get(fileName), downloadStream.ToArray());
                    return remoteFile;
                }

            }
            catch (Exception)
            {
                return null;
            }
            finally
            {

                client.Disconnect();
            }
        }

        //TODO: NEW TEXT TO CHECK THAT FILE IS GENERATED IN REDIS AND CAN BE READ

        Action<string> GetCreateInputFunc(SftpConfiguration sftpConfig, SftpClient client)
        {
            return delegate(string fileName)
            {
                createInputFile(sftpConfig, client, fileName);
            };
        }

        [Test]
        public void Run_ConsumeAllToError_ErrorFilesOnSftpServer()
        { //TRY TO DO WITH ANNOTATION
            Setup();

            int retries = 4;
            int delay = 2;
            int testFilesCount = 4;
            string plainTextFileContent = "This is the content of the ErrorFiles generated on the Sftp Server on Error";
            byte[] errorFileContent = createDummyErrorFileContent(plainTextFileContent);
            string b64ErrorFileContent = makeBase64Chunky(errorFileContent);



            //SETUP
            IConfiguration config = setupConfig();
            SftpConfiguration sftpConfig = createSftpConfig(config);
            SftpClient client = createClient(sftpConfig);
            RedisPipelineManager redisTestPipelineManager = new RedisPipelineManager(config);

            Assert.True(redisTestPipelineManager.Initialize());

            var createInputFile_ = GetCreateInputFunc(sftpConfig, client);

            TestContext.WriteLine("Asserting if input directory is empty at beginning of the test");
            List<string> inputDirectoryFilesEmpty = getInputDirectoryFiles(sftpConfig, client);
            Assert.Zero(inputDirectoryFilesEmpty.Count);

            createInputFile_( "acceptanceTestFile1");
            createInputFile_( "acceptanceTestFile2");
            createInputFile_("acceptanceTestFile3");
            createInputFile_( "acceptanceTestFile4");

        
            Thread.Sleep(delay * 1000);

            List<RemoteFile> cachedFiles = new List<RemoteFile>();


       //     List<(string id, RemoteFile File)> tupletest = new List<(string id, RemoteFile File)>()
       //     {
       //         ("1", null)
       //
       //     };
       //     tupletest[0].id 
       //         tupletest[0].File


            int i = 0;
        
            cachedFiles = redisTestPipelineManager.GetCachedFiles();
            while (cachedFiles.Count < testFilesCount)
            {
                if (i == retries)
                {
                    break;
        
                }
                List<RemoteFile> latestCache = redisTestPipelineManager.GetCachedFiles();

                //If i overwrite the cachedFiles variable with the current cash, data might have been removed in the meantime
                foreach (RemoteFile file in latestCache)
                {
                    if(!cachedFiles.Any(x => x.Name == file.Name))
                    {
                        cachedFiles.Add(file);
                    }
                }

        
                TestContext.WriteLine($"Waiting {delay} seconds");
        
                Thread.Sleep(delay * 1000);
                i++;
            }
        
        
            if (!cachedFiles.Any())
            {
                TestContext.WriteLine("No data was cached within 90 seconds");
            }
        
            TestContext.WriteLine("Asserting if the generated files have been loaded to cache");
            Assert.AreEqual(testFilesCount, cachedFiles.Count);
            List<string> errorFileNames = new List<string>();
            foreach (RemoteFile file in cachedFiles)
            {
                TestContext.WriteLine("Writing FILE: " + file.FullName + " TUID:" + file.TransactionUid + " to ErrorStream");
                string fileNameKey = $"Error:{file.TransactionUid}:Filename";
                string fileContentKey = $"Error:{file.TransactionUid}:File";
                string errorFileName = $"acceptanceTestErrorFile_{file.TransactionUid}";
                redisTestPipelineManager.Set(fileNameKey, errorFileName);
                redisTestPipelineManager.Set(fileContentKey, b64ErrorFileContent);
                
                errorFileNames.Add($"{sftpConfig.ErrorPath}/{errorFileName}");
                redisTestPipelineManager.WriteToErrorStream(file.TransactionUid, "acceptanceTestInducedError");

            }

            List<string> errorDirectoryFiles = new List<string>();

            i = 0;
            errorDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);

            while (errorDirectoryFiles.Count < testFilesCount * 2)
            {
                if (i == retries)
                {
                    break;
                }
                TestContext.WriteLine($"Waiting {delay} seconds");

                Thread.Sleep(delay * 1000);
                errorDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);
                i++;
            }

            if (!errorDirectoryFiles.Any())
            {
                TestContext.WriteLine($"No data was moved to error within {delay * retries} seconds");
            }

            if (errorDirectoryFiles.Count == testFilesCount * 2)
            {
                foreach (string errorFileName in errorFileNames)
                {
                    Assert.Contains(errorFileName,errorDirectoryFiles);
                    RemoteFile errorFile = downloadFile(client,errorFileName);
                    Assert.True(errorFile.Data.SequenceEqual(errorFileContent));
                    string plainText = System.Text.Encoding.UTF8.GetString(errorFile.Data);
                    Assert.AreEqual(plainTextFileContent, plainText);
                }
            }
            else
            {
                
                Assert.Fail("The amount of files found in the Error directory should be double the amount of error files");
            }
            //ASSERT
            TestContext.WriteLine("Asserting that all files have been moved to error");



            Assert.Pass("Acceptance test criteria are met");
        }


        [Test]
        public void Run_ConsumeAllToDone_AllInDone()
        { //TRY TO DO WITH ANNOTATION
            Setup();

            int retries = 4;
            int delay = 5;
            int testFilesCount = 4;
            //SETUP
            IConfiguration config = setupConfig();
            SftpConfiguration sftpConfig = createSftpConfig(config);
            SftpClient client = createClient(sftpConfig);
            RedisPipelineManager redisTestPipelineManager = new RedisPipelineManager(config);

            Assert.True(redisTestPipelineManager.Initialize());

            TestContext.WriteLine("Asserting if input directory is empty at beginning of the test");
            List<string> inputDirectoryFilesEmpty = getInputDirectoryFiles(sftpConfig, client);
            Assert.Zero(inputDirectoryFilesEmpty.Count);

            createInputFile(sftpConfig, client, "acceptanceTestFile1");
            createInputFile(sftpConfig, client, "acceptanceTestFile2");
            createInputFile(sftpConfig, client, "acceptanceTestFile3");
            createInputFile(sftpConfig, client, "acceptanceTestFile4");

            TestContext.WriteLine("Asserting if amount of files in input dir match created amount");
            List<String> sftpInputDirFiles = getInputDirectoryFiles(sftpConfig, client);
            Assert.AreEqual(testFilesCount, sftpInputDirFiles.Count);
            //EXECUTE
            //If handler is running in docker
            Thread.Sleep(delay * 1000);

        //    TestContext.WriteLine("Executing SFTPHANDLER in BACKGROUND");
         //   Task.Run(() => Program.Main(new string[] { }));
            List<RemoteFile> cachedFiles = new List<RemoteFile>();

             int i = 0;
            cachedFiles = redisTestPipelineManager.GetCachedFiles();

            while (cachedFiles.Count < testFilesCount)
            {
                if (i == retries)
                {
                    break;
                }
                TestContext.WriteLine($"Waiting {delay} seconds");
                cachedFiles = redisTestPipelineManager.GetCachedFiles();

                Thread.Sleep(delay * 1000);
                i++;
            } 


            if (!cachedFiles.Any())
            {
                TestContext.WriteLine("No data was cached within 90 seconds");
            }

            TestContext.WriteLine("Asserting if the generated files have been loaded to cache");
            Assert.AreEqual(4, cachedFiles.Count);

            foreach (RemoteFile file in cachedFiles)
            {
                TestContext.WriteLine("Writing FILE: " + file.FullName + " TUID:" + file.TransactionUid + " to ErrorStream");
                redisTestPipelineManager.WriteToDoneStream(file.TransactionUid, "acceptanceTestInducedError");
            }

            List<string> doneDirectoryFiles = new List<string>();

             i = 0;
            doneDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);

            while (doneDirectoryFiles.Any())
            {
                if (i == retries)
                {
                    break;
                }
                TestContext.WriteLine($"Waiting {delay} seconds");

                Thread.Sleep(delay * 1000);
                doneDirectoryFiles = getErrorDirectoryFiles(sftpConfig, client);
                i++;
            } 

            if (doneDirectoryFiles.Any())
            {
                TestContext.WriteLine("No data was deleted from done within 90 seconds");
            }


            List<RemoteFile> cachedFilesAfterRun = new List<RemoteFile>();

            retries = 4; //max 90sec retry
            i = 0; 
            cachedFilesAfterRun = redisTestPipelineManager.GetCachedFiles();

            while (cachedFilesAfterRun.Any())
            {
                if (i == retries)
                {
                    break;
                }
                TestContext.WriteLine($"Waiting {delay} seconds");
                Thread.Sleep(30 * 1000);
                cachedFilesAfterRun = redisTestPipelineManager.GetCachedFiles();
                i++;
            } 

            if (cachedFilesAfterRun.Any())
            {
                TestContext.WriteLine($"The cache was not cleaned after {delay * retries} seconds although no files should be left.");
            }


            
            TestContext.WriteLine("Asserting that all files have been moved to error");

            Assert.AreEqual(0, doneDirectoryFiles.Count);
            Assert.AreEqual(0, cachedFilesAfterRun.Count);

            Assert.Pass("Acceptance test criteria are met");
        }


      
    }
}