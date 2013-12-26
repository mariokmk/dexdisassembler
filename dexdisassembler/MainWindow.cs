using System;
using Gtk;
using System.IO;
using DexDisassembler;
using System.Collections.Generic;
using System.Text;
using dex.net;
using ICSharpCode.SharpZipLib.Zip;
using System.Reflection;
using System.Text.RegularExpressions;

public partial class MainWindow : Gtk.Window
{	
	private Dex _dex;
	private Dictionary<Class,Gtk.NodeStore> _classCache = new Dictionary<Class,Gtk.NodeStore>();
	private string _tempFile = null;
	private TreeModelFilter _filter;
	private Gtk.TreeStore _treeStore;
	private IDexWriter _writer;
	private WritersFactory _factory = new WritersFactory ();
	private List<CodeHighlight> _codeHighlight = new List<CodeHighlight> ();

	private ClassDisplayOptions _classDisplayOptions = ClassDisplayOptions.ClassAnnotations |
		ClassDisplayOptions.ClassName |
		ClassDisplayOptions.ClassDetails |
		ClassDisplayOptions.Fields;


	public MainWindow (): base (Gtk.WindowType.Toplevel)
	{
		Build ();

		//Gdk.Pixbuf = iconbuf;
		//LoadFromResource FromResource ("icon.png").Pixbuf;
		this.Icon = new Gtk.Image(Assembly.GetExecutingAssembly(),  "icon.png").Pixbuf;

		var textRenderer = new Gtk.CellRendererText ();
		var dataColumn = new Gtk.TreeViewColumn ("Data", textRenderer, "text", 0);
		dataColumn.SetCellDataFunc (textRenderer, new Gtk.TreeCellDataFunc (RenderClassOrMethodName));

		treeviewclasses.AppendColumn (dataColumn);
		treeviewclasses.Selection.Changed += OnSelectionChanged;

		textviewCode.ModifyFont(Pango.FontDescription.FromString("monospace 12"));

		PopulateLanguages ();
		comboboxLanguage.Active = 0;

		entrySearch.Changed += OnEntrySearchEditingDone;
	}

	void PopulateLanguages ()
	{
		comboboxLanguage.Clear();
		var cell = new CellRendererText();
		comboboxLanguage.PackStart(cell, false);
		comboboxLanguage.AddAttribute(cell, "text", 0);

		var store = new ListStore(typeof (string));
		comboboxLanguage.Model = store;
		foreach (var lang in _factory.GetWriters()) {
			store.AppendValues (lang);
		}
	}

	protected void OnDeleteEvent (object sender, DeleteEventArgs a)
	{		
		if (_tempFile != null) {
			new FileInfo (_tempFile).Delete ();
		}

		Application.Quit ();
		a.RetVal = true;
	}

	internal void OpenFile(string filename)
	{
		string dexFile = filename;
		_tempFile = null;

		if (System.IO.Path.GetExtension(dexFile).EndsWith("apk")) {
			var apkFile = new FileInfo (dexFile);
			if (apkFile.Exists) {
				var zip = new ZipFile (dexFile);
				var entry = zip.GetEntry ("classes.dex");

				if (entry != null) {
					var zipStream = zip.GetInputStream (entry);
					var tempFileName = System.IO.Path.GetTempFileName ();

					var buffer = new byte[4096];
					using (var writer = File.Create(tempFileName)) {
						int bytesRead;
						while ((bytesRead = zipStream.Read(buffer, 0, 4096)) > 0) {
							writer.Write (buffer, 0, bytesRead);
						}
					}
					dexFile = tempFileName;
					_tempFile = dexFile;
				}
			}
		}

		_dex = new Dex(new FileStream (dexFile, FileMode.Open));
		PopulateClasses();

		_writer.dex = _dex;

		treeviewclasses.GrabFocus();

		labelStatus.Text = filename;
	}

	protected void OnOpenActionActivated (object sender, EventArgs e)
	{
		var fc = new Gtk.FileChooserDialog("Select a DEX file to disassemble",
		                            this,
		                            FileChooserAction.Open,
		                            "Cancel", ResponseType.Cancel,
		                            "Open", ResponseType.Accept);
		
		var filter = new FileFilter();
		filter.Name = "Android Binaries";
		filter.AddPattern("*.dex");
		filter.AddPattern("*.apk");
		fc.AddFilter(filter);
		
		if (fc.Run() == (int)ResponseType.Accept) {
			OpenFile (fc.Filename);
		}

		fc.Destroy();
	}


	protected void OnAboutActionActivated (object sender, EventArgs e)
	{
		AboutDialog about = new AboutDialog();
		
		about.ProgramName = "Dex Disassembler";
		about.Version = "0.5.0";
		about.Authors = new[]{"Mario Kosmiskas"};
		
		about.Run();
		about.Destroy();
	}
	
	protected void OnExitActionActivated (object sender, EventArgs e)
	{
		if (_tempFile != null) {
			new FileInfo (_tempFile).Delete ();
		}

		if (_dex != null) {
			_dex.Dispose();
		}

		Application.Quit ();
	}

	protected void OnCloseActionActivated (object sender, EventArgs e)
	{
		ClearUI();
		
		_dex.Dispose();
		_dex = null;

		if (_tempFile != null) {
			new FileInfo (_tempFile).Delete ();
			_tempFile = null;
		}

		treeviewclasses.GrabFocus();
	}
	
	private void ClearUI()
	{
		_classCache.Clear();
		textviewCode.Buffer.Clear ();
		treeviewclasses.Model = null;
	}

	void RenderClassOrMethodName (TreeViewColumn tree_column, CellRenderer cell, TreeModel tree_model, TreeIter iter)
	{
		var value = tree_model.GetValue (iter, 0);

		if (value is Package) {
			(cell as Gtk.CellRendererText).Text = (value as Package).Name;
		} else if (value is Class) {
			TreeIter parent;
			treeviewclasses.Model.IterParent (out parent, iter);
			var packageName = (treeviewclasses.Model.GetValue (parent, 0) as Package).Name;

			(cell as Gtk.CellRendererText).Text = (value as Class).Name.Replace(packageName + ".", "");
		} else if (value is Method) {
			(cell as Gtk.CellRendererText).Text = (value as Method).Name;
		} else {
			(cell as Gtk.CellRendererText).Text = "Error";
		}
	}

	void OnSelectionChanged (object sender, EventArgs e)
	{
		if (treeviewclasses.Model == null) {
			return;
		}

		TreeIter iter;
		treeviewclasses.Selection.GetSelected (out iter);

		var value = treeviewclasses.Model.GetValue (iter, 0);
		var writer = new StringWriter ();

		if (value is Class) {
			var dexClass = value as Class;
			_writer.WriteOutClass (dexClass, _classDisplayOptions, writer);
		} else if (value is Method) {
			var dexMethod = (Method)value;

			TreeIter parent;
			treeviewclasses.Model.IterParent (out parent, iter);

			_writer.WriteOutMethod ((treeviewclasses.Model.GetValue (parent, 0) as Class), dexMethod, writer, new Indentation(0, 4, ' '), true);
		}
		
		textviewCode.Buffer.Clear();
		textviewCode.Buffer.Text = writer.ToString ();

		// Highlight Code
		foreach (var highlight in _codeHighlight) {
			foreach (Match match in highlight.Expression.Matches(textviewCode.Buffer.Text)) {
				textviewCode.Buffer.ApplyTag (highlight.TagName, textviewCode.Buffer.GetIterAtOffset(match.Groups[1].Index), textviewCode.Buffer.GetIterAtOffset(match.Groups[1].Index+match.Groups[1].Length));
			}
		}
	}

	private void PopulateClasses() 
	{
		var packages = new SortedDictionary<string,List<Class>>();

		// Group classes by package
		foreach (var dexClass in _dex.GetClasses()) {
			var lastDotPosition = dexClass.Name.LastIndexOf ('.');
			string packageName;
			if (lastDotPosition >= 0) {
				packageName = dexClass.Name.Substring (0, dexClass.Name.LastIndexOf ('.'));
			} else {
				packageName = "default";
			}

			List<Class> classes;
			if (!packages.TryGetValue(packageName, out classes)) {
				classes = new List<Class>();
				packages.Add (packageName, classes);
			}

			classes.Add (dexClass);
		}

		// Build the model
		_treeStore = new Gtk.TreeStore (typeof(object), typeof(bool));

		foreach (var package in packages) {
			var iterPackage = _treeStore.AppendValues (new Package(package.Key), true);

			foreach (var dexClass in package.Value) {
				var iter = _treeStore.AppendValues (iterPackage, dexClass, true);

				foreach (var method in dexClass.GetMethods()) {
					_treeStore.AppendValues (iter, method, true);
				}
			}
		}

		_filter = new Gtk.TreeModelFilter (_treeStore, null);
		_filter.VisibleColumn = 1;

		treeviewclasses.Model = _filter;
	}

	protected void OnEntrySearchEditingDone (object sender, EventArgs e)
	{
		if (_dex == null)
			return;

		TreeIter currentClass = new TreeIter();
		TreeIter currentPackage = new TreeIter();

		_treeStore.Foreach ((model, path, iter) => {
			var value = model.GetValue (iter, 0);

			if (value is Package) {
				currentPackage = iter;
				model.SetValue (iter, 1, false);
			} else if (value is Class) {
				currentClass = iter;

				var isVisible = (value as Class).Name.ToLower().IndexOf (entrySearch.Text.ToLower()) > -1;
				model.SetValue (iter, 1, isVisible);
				
				// make sure the package is visible otherwise Gtk won't show this node
				if (isVisible) {
					model.SetValue (currentPackage, 1, true);
				}
			} else if (value is Method) {
				var isVisible = (value as Method).Name.ToLower().IndexOf (entrySearch.Text.ToLower()) > -1;
				model.SetValue (iter, 1, isVisible);

				// make sure the class & paclage are visible otherwise Gtk won't show this node
				if (isVisible) {
					model.SetValue (currentClass, 1, true);
					model.SetValue (currentPackage, 1, true);
				}
			}

			return false;
		});
	}

	protected void LanguageChanged (object sender, EventArgs e)
	{
		_writer = _factory.GetWriter (comboboxLanguage.ActiveText);

		if (_dex != null) {
			_writer.dex = _dex;

			textviewCode.Buffer.TagTable.Foreach (tag => textviewCode.Buffer.TagTable.Remove (tag));
			_codeHighlight.Clear ();
			foreach (var highlight in _writer.GetCodeHightlight()) {
				_codeHighlight.Add (new CodeHighlight (highlight, textviewCode));
			}

			OnSelectionChanged(null, null);
		}
	}

	private class Package
	{
		internal string Name;

		internal Package (string name)
		{
			Name = name;
		}
	}

	private class CodeHighlight
	{
		private static uint Counter;

		internal Regex Expression;
		internal string TagName;

		internal CodeHighlight(HightlightInfo info, TextView view)
		{
			Expression = info.Expression;
			TagName = Counter++.ToString();

			var tag = new TextTag(TagName);
			tag.Foreground = string.Format("#{0}{1}{2}", info.Color.Red.ToString("X2"), info.Color.Green.ToString("X2"), info.Color.Blue.ToString("X2"));
			view.Buffer.TagTable.Add(tag);
		}
	}
}
