using Ravenfall.Updater.Core;
using RavenWeave.Core;
using static System.Net.Mime.MediaTypeNames;

namespace RavenWeave
{
    internal class Program
    {
        private IGameUpdater updater;
        private IKernel kernel;
        public IoC IoC { get; }


        static async Task Main(string[] args)
        {
            var program = new Program();
            await program.RunAsync();
        }

        private async Task RunAsync()
        {
            this.updater = IoC.Resolve<IGameUpdater>();
            this.kernel = IoC.Resolve<IKernel>();

            this.updater.StatusChanged += Updater_StatusChanged;
            this.updater.UpdateCompleted += Updater_UpdateCompleted;
            this.updater.Start();
        }


        private void Updater_UpdateCompleted(object sender, GameUpdateChangedEventArgs e)
        {
            UpdateView(e);
        }

        private void Updater_StatusChanged(object sender, GameUpdateChangedEventArgs e)
        {
            UpdateView(e);

            if (e.Message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.kernel.SetTimeout(() =>
                {
                    Environment.Exit(0);
                }, 4000);
            }

            // shutdown
            // restart, whatnot
        }

        private void UpdateView(GameUpdateChangedEventArgs e)
        {
            Console.Clear();
            Console.WriteLine(e.Message);
            Console.WriteLine($"Progress: {e.Progress:P}");
            Console.WriteLine($"Old Version: {e.OldVersion}");
            Console.WriteLine($"New Version: {e.NewVersion}");
        }


        public Program()
        {
            this.IoC = new IoC();
            this.RegisterModels();
        }

        private void RegisterModels()
        {
            IoC
                .RegisterShared<IKernel, Kernel>()
                .RegisterCustomShared<IActionDispatcher>(() => new ThreadDispatcher())

                .Register<IGameUpdater, GameUpdater>()
                .Register<GameUpdateUnpacker, GameUpdateUnpacker>();
        }
    }

    public class ThreadDispatcher : IActionDispatcher
    {
        public Task BeginInvoke(Action action)
        {
            if (action != null)
            {
                action();
            }

            return Task.CompletedTask;
        }
    }
}
