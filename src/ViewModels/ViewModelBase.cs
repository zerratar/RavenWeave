using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Ravenfall.Updater.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        protected bool Set<T>(ref T prop, T value, [CallerMemberName] string caller = null)
        {
            if (!object.Equals(prop, value))
            {
                prop = value;

                if (PropertyChanged != null)
                {
                    PropertyChanged.Invoke(this, new PropertyChangedEventArgs(caller));
                }

                return true;
            }
            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}