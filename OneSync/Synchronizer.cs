using GalaSoft.MvvmLight.Ioc;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.OneDrive.Sdk;
using System;
using Microsoft.Data.Entity.Infrastructure;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Storage;

namespace OneSync
{
    public class Synchronizer
    {
        private IOneDriveClient client;
        private AccountSession session;
        private string text = "";
        private string executedText = "";

        private const string SYMBOL_commandSeparator = "$$$";
        private const string SYMBOL_parametersSeparator = "~";
        private const string SYMBOL_paramSeparator = ",";
        private const string SYMBOL_paramKeyValueSeparator = ":";

        public async Task Initialize()
        {
            // onedrive
            var scopes = new string[] { "wl.basic", "wl.signin", "onedrive.readwrite" };
            client = OneDriveClientExtensions.GetUniversalClient(scopes);
            session = await client.AuthenticateAsync();
        }

        public async Task UpdateOnedrive()
        {
            await PutOnedrive(text);
        }

        private async Task AppendOnedrive(string newLine)
        {
           // get
           var item = await client
                         .Drive
                         .Root
                         .ItemWithPath("OneSyncTests/test.txt")
                         .Request()
                         .GetAsync();

            if(item == null)
            {
                await PutOnedrive(newLine);
            }
            else
            {
                var content = await client
                         .Drive
                         .Root
                         .ItemWithPath("OneSyncTests/test.txt")
                         .Content
                         .Request()
                         .GetAsync();
                var oldText = "";
                using (var streamReader = new StreamReader(content))
                {
                    oldText = await streamReader.ReadToEndAsync();
                }

                // update
                oldText += Environment.NewLine + newLine;
                await PutOnedrive(text);
            }
        }

        private async Task PutOnedrive(string newLine)
        {
            using(var memoryStream = new MemoryStream())
            using (var streamWriter = new StreamWriter(memoryStream))
            {
                await streamWriter.WriteAsync(newLine);
                await streamWriter.FlushAsync();
                memoryStream.Seek(0, SeekOrigin.Begin);
                var item = await client
                 .Drive
                 .Root
                 .ItemWithPath("OneSyncTests/test.txt")
                 .Content
                 .Request()
                 .PutAsync<Item>(memoryStream);
            }
        }

        private async Task<string> DownloadOnedrive()
        {
            var content = await client
                             .Drive
                             .Root
                             .ItemWithPath("OneSyncTests/test.txt")
                             .Content
                             .Request()
                             .GetAsync();
            var downloadContent = "";
            using (var streamReader = new StreamReader(content))
            {
                downloadContent = await streamReader.ReadToEndAsync();
            }
            return downloadContent;
        }

        public class SyncCommand
        {
            public string Command
            {
                get;
                set;
            }

            public object[] Parameters
            {
                get;
                set;
            }
        }

        private IEnumerable<SyncCommand> ExtractCommands(string rawString)
        {
            // split into commands
            var commandStrings = rawString.Split(new string[] { SYMBOL_commandSeparator }, StringSplitOptions.RemoveEmptyEntries);

            // format commands
            return commandStrings.Select(cs => 
            {
                var command = new SyncCommand();

                // split command and parameters
                var commandParts = cs.Split(new string[] { SYMBOL_parametersSeparator }, StringSplitOptions.RemoveEmptyEntries);

                // add command text
                command.Command = commandParts[0];

                // format parameters
                var parameters = new List<object>();
                if(commandParts.Length>1)
                {
                    // split parameters
                    var parameterParts = commandParts[1].Split(new string[] { SYMBOL_paramSeparator}, StringSplitOptions.RemoveEmptyEntries);
                    
                    // format each pairs
                    foreach(var parameter in parameterParts)
                    {
                        var kvp = parameter.Split(new string[] { SYMBOL_paramKeyValueSeparator }, StringSplitOptions.None);
                        parameters.Add(int.Parse(kvp[1])); // hack: quick test with int
                    }
                }
                command.Parameters = parameters.ToArray();

                // return command
                return command;
            });
        }

        public async Task Download(DbContext db)
        {
            // download raw data
            var rawString = await DownloadOnedrive();

            // format commands 
            var commands = ExtractCommands(rawString);

            // execute database commands
            foreach(var command in commands)
            {
                await db.Database.ExecuteSqlCommandAsync(command.Command, default(System.Threading.CancellationToken), command.Parameters);
            }
        }

        public void Update(object data)
        {
            var logData = data as DbCommandLogData;
            if (logData != null)
            {
                var parameters = "";
                foreach (var p in logData.Parameters)
                {
                    parameters += $"{p.Key}{SYMBOL_paramKeyValueSeparator}{p.Value}{SYMBOL_paramSeparator}";
                }

                var newLine = $"{logData.CommandText}{SYMBOL_parametersSeparator}{parameters}{SYMBOL_commandSeparator}";
                text += Environment.NewLine + newLine;
            }
        }

        public async void ShutDown()
        {
            await client.SignOutAsync();
        }
    }

    public class DbLogger : ISensitiveDataLogger
    {
        public Synchronizer Synchronizer
        {
            get;
            set;
        }

        public bool LogSensitiveData
        {
            get
            {
                return true;
            }
        }

        public DbLogger()
        {
        }

        public void Log(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
        {
            Synchronizer.Update(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScopeImpl(object state)
        {
            return null;
        }
    }


    public class DbLoggerProvider : ILoggerProvider
    {
        private static readonly string[] _whitelist = new string[]
        {
                typeof(Microsoft.Data.Entity.Storage.Internal.RelationalCommandBuilderFactory).FullName,
        };

        public ILogger CreateLogger(string name)
        {
            if (_whitelist.Contains(name))
            {
                return SimpleIoc.Default.GetInstance<DbLogger>();
            }

            return NullLogger.Instance;
        }

        public void Dispose()
        {

        }
    }
    public class DebugLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string name)
        {
            return new DebugLogger(name);
        }

        public void Dispose()
        {

        }
    }


    public class DebugLogger : ISensitiveDataLogger
    {
        private string caller;

        public bool LogSensitiveData
        {
            get
            {
                return true;
            }
        }

        public DebugLogger(string caller)
        {
            this.caller = caller;
        }

        public void Log(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
        {
            Debug.WriteLine($"logLevel: {logLevel}- eventId: {eventId}- message: {formatter(state, exception)}");
        }

        

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public IDisposable BeginScopeImpl(object state)
        {
            return null;
        }
    }

    public class NullLogger : ISensitiveDataLogger
    {
        private static NullLogger _instance = new NullLogger();

        public static NullLogger Instance
        {
            get { return _instance; }
        }

        public bool LogSensitiveData
        {
            get
            {
                return false;
            }
        }

        public void Log(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
        { }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public IDisposable BeginScopeImpl(object state)
        {
            return null;
        }
    }
}
