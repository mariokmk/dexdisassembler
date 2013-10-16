using System;
using Gtk;
using System.Reflection;

namespace DexDisassembler
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			Application.Init ();
			MainWindow win = new MainWindow ();
			if (args.Length > 0) {
				win.OpenFile (args [0]);
			}

			var assembly = Assembly.GetExecutingAssembly ();
			var icon = new Image(assembly.GetManifestResourceStream ("icon.png"));
			win.Icon = icon.Pixbuf;

//			win.SetIconFromFile ("icon.png");

			win.Show ();
			Application.Run ();
		}
	}
}