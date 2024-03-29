﻿using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace System.CommandLine
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public class AliasAttribute : Attribute
    {
        public string Alias { get; }

        public AliasAttribute(string alias)
        {
            Alias = alias;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ArgumentAttribute : Attribute
    {
    }

    public static class COmmandExtensions
    {
        public static Dictionary<string, Argument> AddArguments(this Command command)
        {
            var output = new Dictionary<string, Argument>();

            foreach (var prop in command.GetType().GetProperties())
            {
                var val = prop.GetValue(command);
                if (val is Option option)
                {
                    if (prop.GetCustomAttribute<RequiredAttribute>() is RequiredAttribute)
                    {
                        option.IsRequired = true;
                    }

                    command.Add(option);
                }
                else if (prop.GetCustomAttributes<ArgumentAttribute>().Any())
                {
                    var argumentAttribute = prop.GetCustomAttributes<ArgumentAttribute>().FirstOrDefault();

                    if (argumentAttribute == null)
                        continue;

                    var argument =
                        typeof(COmmandExtensions).GetMethod(nameof(CreateArgument), 1, new[] { typeof(string) })
                            ?.MakeGenericMethod(prop.PropertyType).Invoke(null,
                                new object[]
                                {
                                    prop.GetCustomAttribute<DescriptionAttribute>()?.Description
                                });


                    if (argument is not Argument a) continue;

                    output.Add(prop.Name, a);
                    command.Add(a);
                }
            }

            return output;
        }

        public static Dictionary<string, Option> AddOptions(this Command command)
        {
            var o = new Dictionary<string, Option>();

            foreach (var prop in command.GetType().GetProperties())
            {
                var val = prop.GetValue(command);
                if (val is Option option)
                {
                    if (prop.GetCustomAttribute<RequiredAttribute>() is RequiredAttribute)
                    {
                        option.IsRequired = true;
                    }

                    command.Add(option);
                }
                else if (prop.GetCustomAttributes<AliasAttribute>().Any())
                {
                    var aliass = prop.GetCustomAttributes<AliasAttribute>();
                    var op = typeof(COmmandExtensions)
                        .GetMethod(nameof(CreateOption), 1, new[] { typeof(string), typeof(string) })
                        .MakeGenericMethod(prop.PropertyType).Invoke(null,
                            new object[]
                            {
                                aliass.First().Alias, prop.GetCustomAttribute<DescriptionAttribute>().Description
                            }) as Option;

                    foreach (var a in aliass.Skip(1))
                    {
                        op.AddAlias(a.Alias);
                    }

                    if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType))
                    {
                        op.AllowMultipleArgumentsPerToken = true;
                    }

                    o[prop.Name] = op;

                    command.Add(op);
                }
            }

            return o;
        }

        public static Option<T> CreateOption<T>(string alias, string description)
        {
            return new Option<T>(alias, description);
        }

        public static Argument<T> CreateArgument<T>(string description)
        {
            return new Argument<T> { Description = description };
        }

        public static ICommandHandler Create(Command cmd, IEnumerable<Command> commands,
            Func<ParseResult, IConsole, Task<int>> runner)
        {
            foreach (var command in commands)
                cmd.Add(command);

            var options = cmd.AddOptions();
            var arguments = cmd.AddArguments();

            Task<int> Run(ParseResult parsed, IConsole console)
            {
                foreach (var o in options)
                {
                    cmd.GetType().GetProperty(o.Key).SetValue(cmd, parsed.GetValueForOption(o.Value));
                }

                foreach (var (key, argument) in arguments)
                {
                    cmd.GetType().GetProperty(key)!.SetValue(cmd, parsed.GetValueForArgument(argument));
                }

                return runner(parsed, console);
            }


            return CommandHandler.Create<ParseResult, IConsole>(Run);
        }
    }

    internal sealed class ConsoleHostedService<TApp> : IHostedService where TApp : RootCommand
    {
        private readonly IHostApplicationLifetime appLifetime;
        private readonly TApp app;
        public int Result = 0;

        public ConsoleHostedService(
            IHostApplicationLifetime appLifetime,
            IServiceProvider serviceProvider,
            TApp app)
        {
            this.appLifetime = appLifetime;
            this.app = app;
            //...
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return app.InvokeAsync(System.Environment.GetCommandLineArgs().Skip(1).ToArray())
                .ContinueWith(result =>
                {
                    Result = result.Result;
                    appLifetime.StopApplication();
                });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public static class Extensions
    {
        public static T GetValue<T>(this Argument<T> argument, ParseResult parser)
            => parser.GetValueForArgument(argument);

        public static T GetValue<T>(this Option<T> argument, ParseResult parser)
            => parser.GetValueForOption(argument);

        public static IServiceCollection AddCommand<TCommand>(this IServiceCollection services) where TCommand : Command
        {
            return services.AddSingleton<Command, TCommand>();
        }

        public static IServiceCollection AddConsoleApp<TCommand>(this IServiceCollection services)
            where TCommand : RootCommand
        {
            services.AddSingleton<ConsoleHostedService<TCommand>>();
            services.AddHostedService(sp => sp.GetRequiredService<ConsoleHostedService<TCommand>>());
            return services.AddSingleton<TCommand>();
        }

        public static IHostBuilder AddConsoleApp<TCommand>(this IHostBuilder hostbuilder) where TCommand : RootCommand
        {
            hostbuilder.ConfigureServices((_, services) => services.AddConsoleApp<TCommand>());

            return hostbuilder;
        }

        public static async Task<int> RunConsoleApp<TApp>(this IHost host) where TApp : RootCommand
        {
            var app = host.Services.GetRequiredService<ConsoleHostedService<TApp>>();
            await host.RunAsync();
            return app.Result;
        }
    }
}
