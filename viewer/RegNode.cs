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


        // Data (o anno) all'inizio del nome: "07/03/1840-30/04/1840", "7/3/1840", "1876-1885", "1840 bis"
        static readonly System.Text.RegularExpressions.Regex leadingDate =
            new System.Text.RegularExpressions.Regex(@"^\s*(?:(\d{1,2})/(\d{1,2})/(\d{4})|(\d{4}))(?!\d)");

        /// <summary>Prima data del nome, null se il nome non comincia con una data</summary>
        static DateTime? ParseFirstDate(string caption) {
            var m = leadingDate.Match(caption ?? "");
            if (!m.Success)
                return null;
            try {
                if (m.Groups[4].Success)
                    return new DateTime(int.Parse(m.Groups[4].Value), 1, 1);
                return new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
            }
            catch (ArgumentOutOfRangeException) {
                return null;   // es. 31/02/1840
            }
        }

        /// <summary>
        /// "Riordina nomi": figli con un nome-data in ordine cronologico (prima data del nome); quelli
        /// senza data dopo, in ordine alfabetico; a parita' resta l'ordine di prima. Prima un solo nome
        /// non-data ("(A-I)", "10118, Parte 1", un nodo rinominato) mandava tutto in FormatException.
        /// </summary>
        public static void rielaboraNomi(TreeNode parent) {
            if (parent == null)
                return;
            if (parent.Nodes.Count <= 1)
                return;
            var allChildNodes = (from TreeNode n in parent.Nodes select n).ToList();
            var ordereddNodes = allChildNodes
                .Select(n => new { n, d = ParseFirstDate(n.Text) })
                .OrderBy(x => x.d.HasValue ? 0 : 1)
                .ThenBy(x => x.d ?? DateTime.MinValue)
                .ThenBy(x => x.d.HasValue ? "" : x.n.Text, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.n)
                .ToList();
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
