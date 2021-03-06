﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.IO;

namespace XamlImageConverter {

	public static class XamlScene {

		static XNamespace xic = Parser.xic;
		static XNamespace sb = Parser.sb;
		static string[] validAttributes = new string[] { "Element", "Storyboard", "Frames", "Filmstrip", "Dpi", "RenderDpi", "Quality", "Filename", "Left", "Top", "Right", "Bottom", "Width", "Height", "Cultures", "RenderTimeout", "Page", "FitToPage", "File", "Loop", "Pause", "Skin", "Theme", "Type", "Image", "Verbose", "Parallel", "Ghost", "RenderMode" };

		public static void ApplyParameters(Compiler compiler, XElement e, string filename, Dictionary<string, string> parameters) {
			var type = "png";
			int? hash = null;
			bool nohash = false;

			foreach (var key in parameters.Keys.ToList()) {

				if (validAttributes.Any(a => a == key)) {
					if (key == "Image" || key == "File" || key == "Filename") nohash = true;
					else if (key == "Type") type = parameters["Type"];
					else {
						hash = (hash ?? 0) + Hash.Compute(key + "=" + parameters[key]);
					}
					e.SetAttributeValue(key, parameters[key]);
					parameters.Remove(key);
				} else {
					hash = (hash ?? 0) + Hash.Compute(key + "=" + parameters[key]);
					if (key == "Source") parameters.Remove(key);
				}
			}
			if (hash != null && !nohash) {
				compiler.hash = hash;
				e.SetAttributeValue("Hash", hash);
			}
			if (!nohash && filename != null) {
				if (filename.StartsWith("http://") || filename.StartsWith("https://")) {
					if (filename.Contains('?')) filename = filename.Substring(0, filename.IndexOf('?'));
					filename = filename.Substring(filename.LastIndexOf('/') + 1);
				}
				e.SetAttributeValue("File", filename + "." + type);
			}
		}

		public static void ParseXaml(Compiler compiler, XElement xaml, XElement scene) {
			var isnapshots = xaml.DescendantsAndSelf()
				.Where(e => e.Attributes().Any(a => a.Name.Namespace == xic));
			foreach (var isn in isnapshots) {
				var name = isn.Attribute(Compiler.xxamlns + "Name") ?? isn.Attribute("Name");
				if (name == null && isn != xaml) {
					compiler.Errors.Error("Xaml element for snapshot must be named or root element.", "35", isn);
					continue;
				}
				var sn = new XElement(xic + "Snapshot");
				if (name != null) sn.Add(new XAttribute("Element", name.Value));
				foreach (var ia in isn.Attributes().Where(a => a.Name.Namespace == xic)) {
					sn.Add(new XAttribute(ia.Name.LocalName, ia.Value));
				}
				scene.Add(sn);
			}
		}

		public static XElement CreateDirect(Compiler compiler, string filename, Dictionary<string, string> parameters, string outputPath = null) {
			XElement source = null;
			if (!filename.StartsWith("http://") && !filename.StartsWith("https://")) {
				var file = compiler.MapPath(filename);
				using (compiler.FileLock(file)) {
					source = XElement.Load(file, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo);
				}
				if (source.Name == xic + "XamlImageConverter" || source.Name == sb + "SkinBuilder") {
					foreach (var key in validAttributes) parameters.Remove(key);
					return source;
				} else if (source.Name.LocalName == "XamlImageConverter" || source.Name.LocalName == "SkinBuilder") {
					compiler.Errors.Error("Invalid xml namespace", "70", source);
				}
			}
			XElement snapshot, scene;
			var res = new XElement(xic + "XamlImageConverter",
					new XAttribute(XNamespace.Xmlns+"xic", xic.NamespaceName),
					scene = new XElement(xic+"Scene",
						new XAttribute("OutputPath", outputPath ?? Path.GetDirectoryName(filename.Replace("/", "\\")).Replace("\\", "/")),
						new XAttribute("Source", filename)
					)
				);
			if (parameters.Count > 0) {
				if (!parameters.Keys.Contains("xic")) {
					scene.Add(snapshot = new XElement(xic + "Snapshot"));
					ApplyParameters(compiler, snapshot, null, parameters);
				} else parameters.Remove("xic");
			}
			if (source != null) ParseXaml(compiler, source, scene);
			return res;
		}
		
		public static XElement CreateDirect(Compiler compiler, string filename, XElement e, Dictionary<string, string> parameters, string outputPath = null) {
			XElement scene;
			if (e.Name != xic + "XamlImageConverter") {
				XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
				var res = new XElement(xic + "XamlImageConverter",
						new XAttribute("xmlns", Parser.xamlns),
						new XAttribute(XNamespace.Xmlns+"x", Parser.xxamlns.NamespaceName),
						new XAttribute(XNamespace.Xmlns+"xic", xic.NamespaceName),
						new XAttribute(XNamespace.Xmlns+"mc", mc.NamespaceName), 
						new XAttribute(XNamespace.Xmlns+"d", "http://schemas.microsoft.com/expression/blend/2008"), 
						new XAttribute(mc+"Ignorable", "d"), 
						scene = new XElement(xic+"Scene", new XElement(xic+"Xaml", e))
					);
				if (outputPath != null) scene.Add(new XAttribute("OutputPath", outputPath));
				if (e.Name.NamespaceName == "") {
					e.Name = Parser.xamlns + e.Name.LocalName;
					foreach (var child in e.Descendants()) {
						if (child.Name.NamespaceName == "") child.Name = Parser.xamlns + child.Name.LocalName;
					}
				}
				if (parameters.ContainsKey("Image") || parameters.ContainsKey("File") || parameters.ContainsKey("Filename") || parameters.ContainsKey("Type")) {
					var snapshot = new XElement(xic + "Snapshot");
					ApplyParameters(compiler, snapshot, filename, parameters);
					scene.Add(snapshot);
				}
				ParseXaml(compiler, e, scene);
				return res;
			} else {
				return e;
			}
		}

		public static XElement CreateAxd(Compiler compiler, string filename, Dictionary<string, string> par) {
			var src = par["Source"];
			if (string.IsNullOrEmpty(src)) {
				compiler.Errors.Error("Source cannot be empty.", "32", new TextSpan());
				return null;
			}
			if (src.Trim()[0] == '#') src = (string)compiler.Context.Session[src];
			var outputPath = Path.GetDirectoryName(filename);
			if (!src.Trim().StartsWith("<")) return CreateDirect(compiler, src, par, outputPath);
			return CreateDirect(compiler, "xic.axd" , XElement.Parse(src, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo), par, outputPath);
		}
	}
}
