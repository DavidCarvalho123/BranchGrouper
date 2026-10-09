using BranchGrouper.BranchGroupsWindow;
using Microsoft.VisualStudio.Extensibility.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BranchGrouper
{
    internal class BranchGroupsContent : RemoteUserControl
    {
        public BranchGroupsContent(BranchGroupsData data) : base(dataContext: data) {  }
    }
}
