// ------------------------------------------------------------------------------
//  <autogenerated>
//      This code was generated by a tool.
//      Mono Runtime Version: 2.0.50727.42
// 
//      Changes to this file may cause incorrect behavior and will be lost if 
//      the code is regenerated.
//  </autogenerated>
// ------------------------------------------------------------------------------

namespace Gedcom.UI.GTK.Widgets {
    
    
    public partial class IndividualList {
        
        private Gtk.VBox vbox1;
        
        private Gtk.HBox hbox1;
        
        private Gtk.ComboBox FilterComboBox;
        
        private Gtk.Entry NameEntry;
        
        private Gtk.HBox hbox2;
        
        private Gtk.CheckButton SoundexCheckBox;
        
        private Gtk.Label TotalLabel;
        
        private Gtk.ScrolledWindow scrolledwindow1;
        
        private Gtk.TreeView IndividualTreeView;
        
        protected virtual void Build() {
            Stetic.Gui.Initialize(this);
            // Widget Gedcom.UI.GTK.Widgets.IndividualList
            Stetic.BinContainer.Attach(this);
            this.Events = ((Gdk.EventMask)(256));
            this.Name = "Gedcom.UI.GTK.Widgets.IndividualList";
            // Container child Gedcom.UI.GTK.Widgets.IndividualList.Gtk.Container+ContainerChild
            this.vbox1 = new Gtk.VBox();
            this.vbox1.Name = "vbox1";
            this.vbox1.Spacing = 6;
            this.vbox1.BorderWidth = ((uint)(6));
            // Container child vbox1.Gtk.Box+BoxChild
            this.hbox1 = new Gtk.HBox();
            this.hbox1.Name = "hbox1";
            this.hbox1.Spacing = 6;
            // Container child hbox1.Gtk.Box+BoxChild
            this.FilterComboBox = Gtk.ComboBox.NewText();
            this.FilterComboBox.AppendText("Surname");
            this.FilterComboBox.AppendText("First name");
            this.FilterComboBox.Name = "FilterComboBox";
            this.FilterComboBox.Active = 0;
            this.hbox1.Add(this.FilterComboBox);
            Gtk.Box.BoxChild w1 = ((Gtk.Box.BoxChild)(this.hbox1[this.FilterComboBox]));
            w1.Position = 0;
            w1.Expand = false;
            w1.Fill = false;
            // Container child hbox1.Gtk.Box+BoxChild
            this.NameEntry = new Gtk.Entry();
            this.NameEntry.CanFocus = true;
            this.NameEntry.Name = "NameEntry";
            this.NameEntry.IsEditable = true;
            this.NameEntry.InvisibleChar = '●';
            this.hbox1.Add(this.NameEntry);
            Gtk.Box.BoxChild w2 = ((Gtk.Box.BoxChild)(this.hbox1[this.NameEntry]));
            w2.Position = 1;
            this.vbox1.Add(this.hbox1);
            Gtk.Box.BoxChild w3 = ((Gtk.Box.BoxChild)(this.vbox1[this.hbox1]));
            w3.Position = 0;
            w3.Expand = false;
            w3.Fill = false;
            // Container child vbox1.Gtk.Box+BoxChild
            this.hbox2 = new Gtk.HBox();
            this.hbox2.Name = "hbox2";
            this.hbox2.Spacing = 6;
            // Container child hbox2.Gtk.Box+BoxChild
            this.SoundexCheckBox = new Gtk.CheckButton();
            this.SoundexCheckBox.CanFocus = true;
            this.SoundexCheckBox.Name = "SoundexCheckBox";
            this.SoundexCheckBox.Label = "Use Soundex Matching";
            this.SoundexCheckBox.DrawIndicator = true;
            this.SoundexCheckBox.UseUnderline = true;
            this.hbox2.Add(this.SoundexCheckBox);
            Gtk.Box.BoxChild w4 = ((Gtk.Box.BoxChild)(this.hbox2[this.SoundexCheckBox]));
            w4.Position = 0;
            // Container child hbox2.Gtk.Box+BoxChild
            this.TotalLabel = new Gtk.Label();
            this.TotalLabel.Name = "TotalLabel";
            this.hbox2.Add(this.TotalLabel);
            Gtk.Box.BoxChild w5 = ((Gtk.Box.BoxChild)(this.hbox2[this.TotalLabel]));
            w5.Position = 2;
            w5.Expand = false;
            w5.Fill = false;
            this.vbox1.Add(this.hbox2);
            Gtk.Box.BoxChild w6 = ((Gtk.Box.BoxChild)(this.vbox1[this.hbox2]));
            w6.Position = 1;
            w6.Expand = false;
            w6.Fill = false;
            // Container child vbox1.Gtk.Box+BoxChild
            this.scrolledwindow1 = new Gtk.ScrolledWindow();
            this.scrolledwindow1.CanFocus = true;
            this.scrolledwindow1.Name = "scrolledwindow1";
            this.scrolledwindow1.HscrollbarPolicy = ((Gtk.PolicyType)(2));
            this.scrolledwindow1.ShadowType = ((Gtk.ShadowType)(1));
            // Container child scrolledwindow1.Gtk.Container+ContainerChild
            this.IndividualTreeView = new Gtk.TreeView();
            this.IndividualTreeView.CanFocus = true;
            this.IndividualTreeView.Name = "IndividualTreeView";
            this.IndividualTreeView.FixedHeightMode = true;
            this.IndividualTreeView.Reorderable = true;
            this.IndividualTreeView.RulesHint = true;
            this.IndividualTreeView.SearchColumn = 0;
            this.scrolledwindow1.Add(this.IndividualTreeView);
            this.vbox1.Add(this.scrolledwindow1);
            Gtk.Box.BoxChild w8 = ((Gtk.Box.BoxChild)(this.vbox1[this.scrolledwindow1]));
            w8.Position = 2;
            this.Add(this.vbox1);
            if ((this.Child != null)) {
                this.Child.ShowAll();
            }
            this.Show();
            this.FilterComboBox.Changed += new System.EventHandler(this.OnFilterComboBox_Changed);
            this.NameEntry.Changed += new System.EventHandler(this.OnNameEntry_Changed);
        }
    }
}
