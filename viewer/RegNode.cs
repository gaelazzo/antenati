using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Globalization;

namespace viewer {
    public enum tipoNodo { Archivio, Fondo, Serie, AnnoSerie, AnnoSerieKind, Registro }
    public class RegNode {
        public tipoNodo tipo { get; set; }
        public string title { get { return archiveNode.description; } set {archiveNode.description=value; } }
        public string key {  get; set; }
        public IArchiveNode archiveNode = null;


        static DateTime ParseFirstDate(string caption) {
            // Splitta la stringa sul carattere "-" per identificare la prima data
            var parts = caption.Split('-');
            string firstDate = parts[0];
            if (parts.Length == 1) {
                if (firstDate.Contains("/")) {
                    // Singola data
                    return DateTime.ParseExact(firstDate, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                return DateTime.ParseExact(firstDate, "yyyy", CultureInfo.InvariantCulture);


            }
            else {
                // Intervallo, prendi la prima data
                if (firstDate.Contains("/")) {
                    // Singola data
                    return DateTime.ParseExact(firstDate, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                return DateTime.ParseExact(firstDate, "yyyy", CultureInfo.InvariantCulture);
            }
        }

        static void rielaboraNomi(TreeNode parent) {
            if (parent == null)
                return;
            if (parent.Nodes.Count <= 1)
                return;
            var allChildNodes = (from TreeNode n in parent.Nodes select n).ToList();
            var ordereddNodes = allChildNodes.OrderBy(node => ParseFirstDate(node.Text)).ToList();
            parent.Nodes.Clear();
            foreach (var n in ordereddNodes) {
                parent.Nodes.Add(n);
            }

        }

        public static void fillTree(TreeNodeCollection roots ) {
            //Inserisce i nodi per livello
            roots.Clear();
            //foreach(var a in Archivio.archivi) {
              //  addArchiveNodeToTree(a,roots);
            //}
            foreach (var a in Fondo.fondi) {
                var archivio = Archivio.archivioById[a.idArchivio];
                addArchiveNodeToTree(archivio, roots);//si accerta che ci sia l'archivio
                addArchiveNodeToTree(a, roots);
            }
            
            foreach (var a in Serie.series) {
                var serieNode = addArchiveNodeToTree(a, roots);
                var aatemp = AnnoSerie.annoSerieById;
                foreach(AnnoSerie aa in a.myAnnoSeries) {
                    addArchiveNodeToTree(aa, roots);
                }
                rielaboraNomi(serieNode);
            }
            foreach (var a in AnnoSerieKind.annoSeriesKind) {
                addArchiveNodeToTree(a, roots);
            }
            foreach (var a in Registro.registri) {
                addArchiveNodeToTree(a, roots);
            }
        }



        public RegNode(IArchiveNode node) {
            archiveNode = node;
            tipo = node.tipo;
            title = node.description;
            key = node.key;            
        }

        public static TreeNode addArchiveNodeToTree(IArchiveNode node, TreeNodeCollection roots) {
            var parent = node.parentElement;            
            if (parent != null) {
                var parentNode = addArchiveNodeToTree(parent, roots);
                roots = parentNode.Nodes;
            }

            //check if the archive is already there            
            foreach (TreeNode n in roots) {
                RegNode r = n.Tag as RegNode;
                if (r.archiveNode.key == node.key)
                    return n;
            }
            var rNode = new RegNode(node);
            TreeNode tn = new TreeNode();
            tn.Text = node.description;
            tn.Tag = rNode;
            tn.ToolTipText =rNode.key;
            roots.Add(tn);
            return tn;
        }




        

    }
}
