
using Ravenfall.Updater.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RavenWeave
{
    public class ThreadDispatcher : IActionDispatcher
    {
        private Dispatcher winDispatcher;

        public ThreadDispatcher() { }

        public ThreadDispatcher(System.Windows.Threading.Dispatcher winDispatcher)
        {
            this.winDispatcher = winDispatcher;
        }

        public Task BeginInvoke(Action action)
        {
            if (winDispatcher != null)
            {
                return winDispatcher.BeginInvoke(action).Task;
            }
            else
            {
                if (action != null)
                    action();

                return Task.CompletedTask;
            }
        }
    }
}
