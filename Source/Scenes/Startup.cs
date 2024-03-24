using Celeste64.Mod.Data;
using System.Text.Json;

namespace Celeste64;

/// <summary>
/// Creates a slight delay so the window looks OK before we load Assets
/// TODO: Would be nice if Foster could hide the Window till assets are ready.
/// </summary>
public class Startup : Scene
{
	private int loadDelay = 5;

	private void BeginGame()
	{
		// load save file
		{
			SaveManager.Instance.LoadSaveByFileName(SaveManager.Instance.GetLastLoadedSave());
		}

		// load settings file
		{
			Settings.LoadSettingsByFileName(Settings.DefaultFileName);
		}

		// load assets
		// this currently needs to happen after the save file loads, because this also loads mods, which get their saved settings from the save file.
		Assets.Load();

		// make sure the active language is ready for use,
		// since the save file may have loaded a different language than default.
		Language.Current.Use();

		// try to load controls, or overwrite with defaults if they don't exist
		{
			var controlsFile = Path.Join(App.UserPath, ControlsConfig_V01.FileName);

			ControlsConfig_V01? controls = null;
			if (File.Exists(controlsFile))
			{
				try
				{
					controls = JsonSerializer.Deserialize(File.ReadAllText(controlsFile), ControlsConfig_V01Context.Default.ControlsConfig_V01);
				}
				catch
				{
					controls = null;
				}
			}

			// create defaults if not found
			if (controls == null)
			{
				controls = ControlsConfig_V01.Defaults;
				using var stream = File.Create(controlsFile);
				JsonSerializer.Serialize(stream, ControlsConfig_V01.Defaults, ControlsConfig_V01Context.Default.ControlsConfig_V01);
				stream.Flush();
			}

			Controls.Load(controls);
		}

		// enter game
		//Assets.Levels[0].Enter(new AngledWipe());
		Game.Instance.Goto(new Transition()
		{
			Mode = Transition.Modes.Replace,
			Scene = () => new Titlescreen(),
			ToBlack = null,
			FromBlack = new AngledWipe(),
		});
	}

	public override void Update()
	{
		if (loadDelay > 0)
		{
			loadDelay--;
			if (loadDelay <= 0)
				BeginGame();
		}
	}

	public override void Render(Target target)
	{
		target.Clear(Color.Black);
	}
}
