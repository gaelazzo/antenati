using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace viewer {
    public interface IArchiveNode {
        string key { get; }
        string parentKey { get; }

        IArchiveNode parentElement { get; }
        string description { get; set; }
        string href {  get; }

        /// <summary>
        /// Read all child nodes
        /// </summary>
        /// <returns></returns>
        List<IArchiveNode> explore();
        tipoNodo tipo { get; }
    }
}
