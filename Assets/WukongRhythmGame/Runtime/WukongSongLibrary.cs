using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Wukong Rhythm/Song Library", fileName = "WukongSongLibrary")]
public sealed class WukongSongLibrary : ScriptableObject
{
    public List<WukongSongDefinition> songs = new List<WukongSongDefinition>();

    public int Count => songs != null ? songs.Count : 0;

    public WukongSongDefinition Get(int index)
    {
        if (songs == null || songs.Count == 0)
        {
            return null;
        }

        return songs[Mathf.Clamp(index, 0, songs.Count - 1)];
    }

    public void RemoveNullEntries()
    {
        if (songs == null)
        {
            songs = new List<WukongSongDefinition>();
            return;
        }

        songs.RemoveAll(song => song == null || song.audioClip == null);
    }
}
