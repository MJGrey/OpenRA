#region Copyright & License Information
/*
 * Copyright 2007-2016 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class ModBrowserLogic : ChromeLogic
	{
		readonly Widget modList;
		readonly ButtonWidget modTemplate;
		readonly Manifest[] allMods;
		readonly Dictionary<string, Sprite> previews = new Dictionary<string, Sprite>();
		readonly Dictionary<string, Sprite> logos = new Dictionary<string, Sprite>();
		readonly Cache<Manifest, ModContent> content = new Cache<Manifest, ModContent>(LoadModContent);

		readonly Widget modChooserPanel;
		readonly ButtonWidget loadButton;
		readonly SheetBuilder sheetBuilder;
		Manifest selectedMod;
		string selectedAuthor;
		string selectedDescription;
		int modOffset = 0;

		static ModContent LoadModContent(Manifest mod)
		{
			return mod.Get<ModContent>(Game.ModData.ObjectCreator);
		}

		[ObjectCreator.UseCtor]
		public ModBrowserLogic(Widget widget, ModData modData)
		{
			modChooserPanel = widget;
			loadButton = modChooserPanel.Get<ButtonWidget>("LOAD_BUTTON");
			loadButton.OnClick = () => LoadMod(selectedMod);
			loadButton.IsDisabled = () => selectedMod.Id == modData.Manifest.Id;

			var contentButton = modChooserPanel.Get<ButtonWidget>("CONFIGURE_BUTTON");
			contentButton.OnClick = () =>
			{
				var widgetArgs = new WidgetArgs
				{
					{ "mod", selectedMod },
					{ "content", content[selectedMod] },
					{ "onCancel", () => { } }
				};

				Ui.OpenWindow("CONTENT_PANEL", widgetArgs);
			};

			modChooserPanel.Get<ButtonWidget>("QUIT_BUTTON").OnClick = Game.Exit;

			modList = modChooserPanel.Get("MOD_LIST");
			modTemplate = modList.Get<ButtonWidget>("MOD_TEMPLATE");

			modChooserPanel.Get<LabelWidget>("MOD_DESC").GetText = () => selectedDescription;
			modChooserPanel.Get<LabelWidget>("MOD_TITLE").GetText = () => selectedMod.Metadata.Title;
			modChooserPanel.Get<LabelWidget>("MOD_AUTHOR").GetText = () => selectedAuthor;
			modChooserPanel.Get<LabelWidget>("MOD_VERSION").GetText = () => selectedMod.Metadata.Version;

			var prevMod = modChooserPanel.Get<ButtonWidget>("PREV_MOD");
			prevMod.OnClick = () => { modOffset -= 1; RebuildModList(); };
			prevMod.IsVisible = () => modOffset > 0;

			var nextMod = modChooserPanel.Get<ButtonWidget>("NEXT_MOD");
			nextMod.OnClick = () => { modOffset += 1; RebuildModList(); };
			nextMod.IsVisible = () => modOffset + 5 < allMods.Length;

			modChooserPanel.Get<RGBASpriteWidget>("MOD_PREVIEW").GetSprite = () =>
			{
				Sprite ret = null;
				previews.TryGetValue(selectedMod.Id, out ret);
				return ret;
			};

			sheetBuilder = new SheetBuilder(SheetType.BGRA);
			allMods = Game.Mods.Values.Where(m => !m.Metadata.Hidden)
				.OrderBy(m => m.Metadata.Title)
				.ToArray();

			// Load preview images, and eat any errors
			foreach (var mod in allMods)
			{
				try
				{
					using (var stream = mod.Package.GetStream("preview.png"))
						using (var preview = new Bitmap(stream))
							if (preview.Width == 296 && preview.Height == 196)
								previews.Add(mod.Id, sheetBuilder.Add(preview));
				}
				catch (Exception) { }

				try
				{
					using (var stream = mod.Package.GetStream("logo.png"))
						using (var logo = new Bitmap(stream))
							if (logo.Width == 96 && logo.Height == 96)
								logos.Add(mod.Id, sheetBuilder.Add(logo));
				}
				catch (Exception) { }
			}

			Manifest initialMod;
			Game.Mods.TryGetValue(Game.Settings.Game.PreviousMod, out initialMod);
			SelectMod(initialMod != null && initialMod.Id != "modchooser" ? initialMod : Game.Mods["ra"]);

			RebuildModList();
		}

		void RebuildModList()
		{
			modList.RemoveChildren();

			var width = modTemplate.Bounds.Width;
			var height = modTemplate.Bounds.Height;
			var innerMargin = modTemplate.Bounds.Left;
			var outerMargin = (modList.Bounds.Width - Math.Min(5, allMods.Length) * width - 4 * innerMargin) / 2;
			var stride = width + innerMargin;

			for (var i = 0; i < 5; i++)
			{
				var j = i + modOffset;
				if (j >= allMods.Length)
					break;

				var mod = allMods[j];

				var item = modTemplate.Clone() as ButtonWidget;
				item.Bounds = new Rectangle(outerMargin + i * stride, 0, width, height);
				item.IsHighlighted = () => selectedMod == mod;
				item.OnClick = () => SelectMod(mod);
				item.OnDoubleClick = () => LoadMod(mod);
				item.OnKeyPress = e =>
				{
					if (e.MultiTapCount == 2)
						LoadMod(mod);
					else
						SelectMod(mod);
				};

				item.TooltipText = mod.Metadata.Title;

				if (j < 9)
					item.Key = new Hotkey((Keycode)((int)Keycode.NUMBER_1 + j), Modifiers.None);

				Sprite logo = null;
				logos.TryGetValue(mod.Id, out logo);
				item.Get<RGBASpriteWidget>("MOD_LOGO").GetSprite = () => logo;
				item.Get("MOD_NO_LOGO").IsVisible = () => logo == null;

				modList.AddChild(item);
			}
		}

		void SelectMod(Manifest mod)
		{
			selectedMod = mod;
			selectedAuthor = "By " + (mod.Metadata.Author ?? "unknown author");
			selectedDescription = (mod.Metadata.Description ?? "").Replace("\\n", "\n");
			var selectedIndex = Array.IndexOf(allMods, mod);
			if (selectedIndex - modOffset > 4)
				modOffset = selectedIndex - 4;
		}

		void LoadMod(Manifest mod)
		{
			var modId = mod.Id;
			if (!Game.IsModInstalled(modId))
			{
				var widgetArgs = new WidgetArgs
				{
					{ "mod", selectedMod },
					{ "content", content[selectedMod] },
				};

				Ui.OpenWindow("INSTALL_MOD_PANEL", widgetArgs);
				return;
			}

			if (!IsModInstalled(mod))
			{
				var widgetArgs = new WidgetArgs
				{
					{ "continueLoading", () =>
						Game.RunAfterTick(() => Game.InitializeMod(modId, new Arguments())) },
					{ "mod", selectedMod },
					{ "content", content[selectedMod] },
				};

				Ui.OpenWindow("CONTENT_PROMPT_PANEL", widgetArgs);

				return;
			}

			Game.RunAfterTick(() =>
			{
				Ui.CloseWindow();
				sheetBuilder.Dispose();
				Game.InitializeMod(modId, null);
			});
		}

		bool IsModInstalled(Manifest mod)
		{
			return content[mod].Packages
				.Where(p => p.Value.Required)
				.All(p => p.Value.TestFiles.All(f => File.Exists(Platform.ResolvePath(f))));
		}
	}
}
