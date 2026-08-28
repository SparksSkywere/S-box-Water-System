using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox.Editor;

/// <summary>
/// Saved River Path overlay layout (section expand/collapse and hide/show).
/// </summary>
public sealed class RiverPathToolWindowSettings
{
	static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public Dictionary<string, bool> ExpandedSections { get; set; } = new();
	public bool WindowHidden { get; set; }

	static string SettingsPath
	{
		get
		{
			var root = Project.Current?.GetRootPath();
			if ( string.IsNullOrWhiteSpace( root ) )
				root = Directory.GetCurrentDirectory();

			return Path.Combine( root, "Editor", "Water", "river_path_tool_settings.json" );
		}
	}

	public bool GetExpanded( string section, bool defaultValue )
		=> ExpandedSections.TryGetValue( section, out var expanded ) ? expanded : defaultValue;

	public void SetExpanded( string section, bool expanded )
	{
		ExpandedSections[section] = expanded;
		Save();
	}

	public void SetWindowHidden( bool hidden )
	{
		WindowHidden = hidden;
		Save();
	}

	public static RiverPathToolWindowSettings Load()
	{
		try
		{
			if ( !File.Exists( SettingsPath ) )
				return new RiverPathToolWindowSettings();

			var json = File.ReadAllText( SettingsPath );
			return JsonSerializer.Deserialize<RiverPathToolWindowSettings>( json ) ?? new RiverPathToolWindowSettings();
		}
		catch ( Exception )
		{
			return new RiverPathToolWindowSettings();
		}
	}

	public void Save()
	{
		try
		{
			var directory = Path.GetDirectoryName( SettingsPath );
			if ( !string.IsNullOrWhiteSpace( directory ) )
				Directory.CreateDirectory( directory );

			File.WriteAllText( SettingsPath, JsonSerializer.Serialize( this, JsonOptions ) );
		}
		catch ( Exception )
		{
		}
	}
}
