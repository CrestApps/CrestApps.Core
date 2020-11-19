using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrestApps.Foundation
{
    public interface IRegisterToContainer
    {
    }

    public interface IRegisterToContainer<T> : IRegisterToContainer
    {
    }
}
