﻿using Ravenfall.Updater.Core;
using Ravenfall.Updater.ViewModels;
using RavenWeave;
using RavenWeave.Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Ravenfall.Updater
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            this.IoC = new IoC();
            this.RegisterModels();
        }

        public IoC IoC { get; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            //var args = e.Args;
            //if (args == null || args.Length == 0)
            //{
            //    MessageBox.Show("Patcher can only be started by Ravenfall.");
            //    this.Shutdown();
            //    return;
            //}
        }

        private void RegisterModels()
        {

            IoC
                .RegisterShared<IKernel, Kernel>()
                .RegisterCustomShared<IActionDispatcher>(() => new ThreadDispatcher(Dispatcher))

                .Register<IGameUpdater, GameUpdater>()
                .Register<GameUpdateUnpacker, GameUpdateUnpacker>()

                // View Models
                .Register<MainViewModel, MainViewModel>();
        }

    }
}
