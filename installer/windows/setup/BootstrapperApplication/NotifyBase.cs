using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GaltekClassroom.Bootstrapper
{
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
