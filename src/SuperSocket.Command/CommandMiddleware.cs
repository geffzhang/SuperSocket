﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SuperSocket.Channel;
using SuperSocket.ProtoBase;

namespace SuperSocket.Command
{
    public class CommandMiddleware<TKey, TNetPackageInfo, TPackageInfo, TPackageMapper> : CommandMiddleware<TKey, TNetPackageInfo, TPackageInfo>
        where TPackageInfo : class, IKeyedPackageInfo<TKey>
        where TNetPackageInfo : class
        where TPackageMapper : IPackageMapper<TNetPackageInfo, TPackageInfo>, new()
    {
        protected override IPackageMapper<TNetPackageInfo, TPackageInfo> GetPackageMapper()
        {
            return new TPackageMapper();
        }

        public CommandMiddleware(IServiceProvider serviceProvider, IOptions<CommandOptions> commandOptions)
            : base(serviceProvider, commandOptions)
        {

        }
    }

    public class CommandMiddleware<TKey, TNetPackageInfo, TPackageInfo> : CommandMiddleware<TKey, TPackageInfo>
        where TPackageInfo : class, IKeyedPackageInfo<TKey>
        where TNetPackageInfo : class
    {

        IPackageMapper<TNetPackageInfo, TPackageInfo> _packageMapper;

        protected virtual IPackageMapper<TNetPackageInfo, TPackageInfo> GetPackageMapper()
        {
            return _packageMapper;
        }

        public CommandMiddleware(IServiceProvider serviceProvider, IOptions<CommandOptions> commandOptions)
            : base(serviceProvider, commandOptions)
        {
            _packageMapper = serviceProvider.GetService<IPackageMapper<TNetPackageInfo, TPackageInfo>>();
        }

        public override void Register(IServer server, IAppSession session)
        {
            var channel = session.Channel as IChannel<TNetPackageInfo>;
            
            if (channel == null)
                throw new Exception("Unmatched package type.");

            var packageMapper = GetPackageMapper();
            
            channel.PackageReceived += async (ch, p) =>
            {
                await OnPackageReceived(session, packageMapper.Map(p));
            };
        }
    }

    public class CommandMiddleware<TKey, TPackageInfo> : MiddlewareBase
        where TPackageInfo : class, IKeyedPackageInfo<TKey>
    {
        private Dictionary<TKey, ICommand<TKey>> _commands;        

        public CommandMiddleware(IServiceProvider serviceProvider, IOptions<CommandOptions> commandOptions)
        {
            var commandInterface = typeof(ICommand<TKey, TPackageInfo>).GetTypeInfo();
            var asyncCommandInterface = typeof(IAsyncCommand<TKey, TPackageInfo>).GetTypeInfo();            
            var commandTypes = commandOptions.Value.GetCommandTypes((t) => commandInterface.IsAssignableFrom(t) || asyncCommandInterface.IsAssignableFrom(t));
            var comparer = serviceProvider.GetService<IEqualityComparer<TKey>>();

            var commands = commandTypes.Select(t =>  ActivatorUtilities.CreateInstance(serviceProvider, t) as ICommand<TKey>);

            if (comparer == null)
                _commands = commands.ToDictionary(x => x.Key);
            else
                _commands = commands.ToDictionary(x => x.Key, comparer);
        }

        public override void Register(IServer server, IAppSession session)
        {
            var channel = session.Channel as IChannel<TPackageInfo>;
            
            if (channel == null)
                throw new Exception("Unmatched package type.");
            
            channel.PackageReceived += async (ch, p) =>
            {
                await OnPackageReceived(session, p);
            };
        }

        protected async Task OnPackageReceived(IAppSession session, TPackageInfo package)
        {
            if (!_commands.TryGetValue(package.Key, out ICommand<TKey> command))
            {
                return;
            }

            var asyncCommand = command as IAsyncCommand<TKey, TPackageInfo>;

            if (asyncCommand != null)
            {
                await asyncCommand.ExecuteAsync(session, package);
                return;
            }

            ((ICommand<TKey, TPackageInfo>)command).Execute(session, package);
        }
    }
}
