﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Celeste64;

public static class Assets
{
	public const float FontSize = Game.RelativeScale * 16;
	public const string AssetFolder = "Content";

	private static string? contentPath = null;

	public static string ContentPath
	{
		get
		{
			if (contentPath == null)
			{
				var baseFolder = AppContext.BaseDirectory;
				var searchUpPath = "";
				int up = 0;
				while (!Directory.Exists(Path.Join(baseFolder, searchUpPath, AssetFolder)) && up++ < 6)
					searchUpPath = Path.Join(searchUpPath, "..");
				if (!Directory.Exists(Path.Join(baseFolder, searchUpPath, AssetFolder)))
					throw new Exception($"Unable to find {AssetFolder} Directory from '{baseFolder}'");
				contentPath = Path.Join(baseFolder, searchUpPath, AssetFolder);
			}

			return contentPath;
		}
	}

	public static readonly ModAssetDictionary<Map> Maps = new(gameMod => gameMod.Maps);
	public static readonly ModAssetDictionary<Shader> Shaders = new(gameMod => gameMod.Shaders);
	public static readonly ModAssetDictionary<Texture> Textures = new(gameMod => gameMod.Textures);
	public static readonly ModAssetDictionary<SkinnedTemplate> Models = new(gameMod => gameMod.Models);
	public static readonly ModAssetDictionary<Subtexture> Subtextures = new(gameMod => gameMod.Subtextures);
	public static readonly ModAssetDictionary<Font> Fonts = new(gameMod => gameMod.Fonts);
	public static readonly ModAssetDictionary<FMOD.Sound> Sounds = new(gameMod => gameMod.Sounds);
	public static readonly ModAssetDictionary<FMOD.Sound> Music = new(gameMod => gameMod.Music);
	public static readonly Dictionary<string, Language> Languages = new(StringComparer.OrdinalIgnoreCase);

	public static List<SkinInfo> Skins { get; private set; } = [];

	public static List<SkinInfo> EnabledSkins { get { return Skins.Where(skin => skin.IsEnabled()).ToList(); } }

	public static List<LevelInfo> Levels { get; private set; } = [];

	public static void Load()
	{
		var timer = Stopwatch.StartNew();

		Levels.Clear();
		Maps.Clear();
		Shaders.Clear();
		Textures.Clear();
		Subtextures.Clear();
		Models.Clear();
		Fonts.Clear();
		Languages.Clear();
		Sounds.Clear();
		Music.Clear();
		Audio.Unload();

		Map.ModActorFactories.Clear();
		ModLoader.RegisterAllMods();

		var maps = new ConcurrentBag<(Map, GameMod)>();
		var images = new ConcurrentBag<(string, Image, GameMod)>();
		var models = new ConcurrentBag<(string, SkinnedTemplate, GameMod)>();
		var sounds = new ConcurrentBag<(string, FMOD.Sound, GameMod)>();
		var music = new ConcurrentBag<(string, FMOD.Sound, GameMod)>();
		var langs = new ConcurrentBag<(Language, GameMod)>();
		var tasks = new List<Task>();

		// NOTE: Make sure to update ModManager.OnModFileChanged() as well, for hot-reloading to work!
		
		var globalFs = ModManager.Instance.GlobalFilesystem;
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Maps", "map"))
		{
			// Skip the "autosave" folder
			if (file.StartsWith("Maps/autosave", StringComparison.OrdinalIgnoreCase))
				continue;

			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, 
					    stream => new Map(GetResourceNameFromVirt(file, "Maps"), file, stream), out var map))
				{
					maps.Add((map, mod));
				}
			}));
		}

		// load texture pngs
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Textures", "png"))
		{
			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryLoadImage(file, out var image))
				{
					images.Add((GetResourceNameFromVirt(file, "Textures"), image, mod));
				}
			}));
		}

		// load faces
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Faces", "png"))
		{
			tasks.Add(Task.Run(() =>
			{
				var name = $"faces/{GetResourceNameFromVirt(file, "Faces")}";
				if (mod.Filesystem != null && mod.Filesystem.TryLoadImage(file, out var image))
				{
					images.Add((name, image, mod));
				}
			}));
		}

		// load glb models
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Models", "glb"))
		{
			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => SharpGLTF.Schema2.ModelRoot.ReadGLB(stream),
						out var input))
				{
					var model = new SkinnedTemplate(input);
					models.Add((GetResourceNameFromVirt(file, "Models"), model, mod));
				}
			}));
		}
		
		// load languages
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Text", "json"))
		{
			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryLoadText(file, out var data))
				{
					if (JsonSerializer.Deserialize(data, LanguageContext.Default.Language) is { } lang)
						langs.Add((lang, mod));
				}				
			}));
		}

		// load audio
		var allBankFiles = globalFs.FindFilesInDirectoryRecursiveWithMod("Audio", "bank").ToList();
		// load strings first
		foreach (var (file, mod) in allBankFiles)
		{
			if (mod.Filesystem != null && file.EndsWith(".strings.bank"))
				mod.Filesystem.TryOpenFile(file, Audio.LoadBankFromStream);
		}
		// load banks second
		foreach (var (file, mod) in allBankFiles)
		{
			if (mod.Filesystem != null && file.EndsWith(".bank") && !file.EndsWith(".strings.bank"))
				mod.Filesystem.TryOpenFile(file, Audio.LoadBankFromStream);
		}

		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Sounds", "wav"))
		{
			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => Audio.LoadWavFromStream(stream),
						out FMOD.Sound? sound))
				{
					if(sound != null)
					{
						sounds.Add((GetResourceNameFromVirt(file, "Sounds"), sound.Value, mod));
					}
				}
			}));
		}

		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Music", "wav"))
		{
			tasks.Add(Task.Run(() =>
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => Audio.LoadWavFromStream(stream),
						out FMOD.Sound? song))
				{
					if (song != null)
					{
						music.Add((GetResourceNameFromVirt(file, "Music"), song.Value, mod));
					}
				}
			}));
		}

		// load level, dialog jsons
		foreach (var mod in ModManager.Instance.Mods)
		{
			mod.Levels.Clear();
			if (mod.Filesystem != null && mod.Filesystem.TryOpenFile("Levels.json", 
				    stream => JsonSerializer.Deserialize(stream, LevelInfoListContext.Default.ListLevelInfo) ?? [], 
				    out var levels))
			{
				mod.Levels.AddRange(levels);
				Levels.AddRange(levels);
			}
			
			// if (mod.Filesystem != null && mod.Filesystem.TryOpenFile("Dialog.json", 
			// 	    stream => JsonSerializer.Deserialize(stream, DialogLineDictContext.Default.DictionaryStringListDialogLine) ?? [], 
			// 	    out var dialog))
			// {
			// 	foreach (var (key, value) in dialog)
			// 	{
			// 		Dialog.Add(key, value, mod);
			// 	}
			// }
		}

		// load glsl shaders
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Shaders", "glsl"))
		{
			if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => LoadShader(file, stream), out var shader))
			{
				shader.Name = GetResourceNameFromVirt(file, "Shaders");
				Shaders.Add(shader.Name, shader, mod);
			}
		}

		// load font files
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Fonts", ""))
		{
			if (file.EndsWith(".ttf") || file.EndsWith(".otf"))
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => new Font(stream), out var font))
					Fonts.Add(GetResourceNameFromVirt(file, "Fonts"), font, mod);
			}
		}

		// pack sprites into single texture
		{
			Packer packer = new Packer
			{
				Trim = false,
				CombineDuplicates = false,
				Padding = 1
			};
			foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Sprites", "png"))
			{
				if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file, stream => new Image(stream), out var img))
				{
					packer.Add($"{mod.ModInfo.Id}:{GetResourceNameFromVirt(file, "Sprites")}", img);
				}
			}

			var result = packer.Pack();
			var pages = new List<Texture>();
			foreach (var it in result.Pages)
			{
				it.Premultiply();
				pages.Add(new Texture(it));
			}

			foreach (var it in result.Entries)
			{
				string[] nameSplit = it.Name.Split(':');
				GameMod? mod = ModManager.Instance.Mods.FirstOrDefault(mod => mod.ModInfo.Id == nameSplit[0]) ?? ModManager.Instance.VanillaGameMod;
				if(mod != null)
				{
					Subtextures.Add(nameSplit[1], new Subtexture(pages[it.Page], it.Source, it.Frame), mod);
				}
			}
		}


		// wait for tasks to finish
		{
			foreach (var task in tasks)
				task.Wait();

			foreach (var (name, img, mod) in images)
				Textures.Add(name, new Texture(img) { Name = name }, mod);
			foreach (var (map, mod) in maps)
				Maps.Add(map.Name, map, mod);
			foreach (var (name, sound, mod) in sounds)
				Sounds.Add(name, sound, mod);
			foreach (var (name, song, mod) in music)
				Music.Add(name, song, mod);
			foreach (var (name, model, mod) in models)
			{
				model.ConstructResources();
				Models.Add(name, model, mod);
			}
			foreach (var (lang, mod) in langs)
			{
				if (Languages.TryGetValue(lang.ID, out var existing))
				{
					existing.Absorb(lang, mod);
				}
				else
				{
					lang.OnCreate(mod);
					Languages.Add(lang.ID, lang);
				}
			}
		}

		// Load Skins
		Skins = [
			new SkinInfo{
				Name = "Madeline",
				Model = "player",
				HideHair = false,
				HairNormal = 0xdb2c00,
				HairNoDash = 0x6ec0ff,
				HairTwoDash = 0xfa91ff,
				HairRefillFlash = 0xffffff,
				HairFeather = 0xf2d450
			}
		];
		foreach (var (file, mod) in globalFs.FindFilesInDirectoryRecursiveWithMod("Skins", "json"))
		{
			if (mod.Filesystem != null && mod.Filesystem.TryOpenFile(file,
				    stream => JsonSerializer.Deserialize(stream, SkinInfoContext.Default.SkinInfo), out var skin) && skin.IsValid())
			{
				skin.ModId = mod.ModInfo.Id;
				Skins.Add(skin);
			}
			else
			{
				Log.Warning($"Improperly configured skin: {file}");
			}
		}

		// make sure the active language is ready for use
		Language.Current.Use();

		ModManager.Instance.OnAssetsLoaded();

		Log.Info($"Loaded Assets in {timer.ElapsedMilliseconds}ms");
	}

	internal static string GetResourceNameFromVirt(string virtPath, string folder)
	{
		var ext = Path.GetExtension(virtPath);
		// +1 to account for the forward slash
		return virtPath.AsSpan((folder.Length+1)..^ext.Length).ToString();
	}

	internal static Shader? LoadShader(string virtPath, Stream file)
	{
		using var reader = new StreamReader(file);
		var code = reader.ReadToEnd();

		StringBuilder vertex = new();
		StringBuilder fragment = new();
		StringBuilder? target = null;
		
		foreach (var l in code.Split('\n'))
		{
			var line = l.Trim('\r');
			
			if (line.StartsWith("VERTEX:"))
				target = vertex;
			else if (line.StartsWith("FRAGMENT:"))
				target = fragment;
			else if (line.StartsWith("#include"))
			{
				var path = $"{Path.GetDirectoryName(virtPath)}/{line[9..]}";

				if (ModManager.Instance.GlobalFilesystem.TryLoadText(path, out var include))
					target?.Append(include);
				else
					throw new Exception($"Unable to find shader include: '{path}'");
			}
			else
				target?.AppendLine(line);
		}

		return new Shader(new(
			vertexShader: vertex.ToString(),
			fragmentShader: fragment.ToString()
		));
	}
}