using System;
using System.Collections.Generic;
using System.IO;

namespace SwanUnderwater
{
    /// <summary>Offline single-syllable Han selection. Data is MIT pinyin-data, not a nickname preset list.</summary>
    public sealed class OceanPinyinInput
    {
        public const int PageSize=8;
        readonly Dictionary<string,string> table=new Dictionary<string,string>(StringComparer.Ordinal);
        readonly Dictionary<string,string[]> scalarTable=new Dictionary<string,string[]>(StringComparer.Ordinal);
        string[] Scalars=>scalarTable.TryGetValue(Composition,out string[] value)?value:Array.Empty<string>();
        public string Composition { get; private set; }="";
        public int Page { get; private set; }
        public int SyllableCount=>table.Count;
        public string Candidates=>table.TryGetValue(Composition,out string value)?value:"";
        public int PageCount=>Math.Max(1,(Scalars.Length+PageSize-1)/PageSize);
        public OceanPinyinInput(string data)
        {
            if(string.IsNullOrEmpty(data))throw new ArgumentException("Pinyin dictionary is missing.");
            using(var reader=new StringReader(data))
            {
                string line;
                while((line=reader.ReadLine())!=null)
                {
                    if(line.Length==0||line[0]=='#')continue;
                    int split=line.IndexOf('\t');
                    if(split<1||split>6||split==line.Length-1)throw new FormatException("Invalid pinyin dictionary row.");
                    string key=line.Substring(0,split),characters=line.Substring(split+1);
                    foreach(char c in key)if(c<'a'||c>'z')throw new FormatException("Non-ASCII pinyin key.");
                    var scalars=new List<string>();
                    for(int i=0;i<characters.Length;i++)
                    {
                        char c=characters[i];int length=1;
                        if(char.IsHighSurrogate(c)) {if(i+1>=characters.Length||!char.IsLowSurrogate(characters[i+1]))throw new FormatException("Invalid pinyin scalar.");length=2;}
                        else if(char.IsLowSurrogate(c)||char.IsControl(c))throw new FormatException("Invalid pinyin scalar.");
                        scalars.Add(characters.Substring(i,length));i+=length-1;
                    }
                    table.Add(key,characters);scalarTable.Add(key,scalars.ToArray());
                }
            }
            if(table.Count<400)throw new FormatException("Incomplete pinyin dictionary.");
        }
        public bool Append(char letter)
        {
            letter=char.ToLowerInvariant(letter);
            if(letter<'a'||letter>'z'||Composition.Length>=6)return false;
            Composition+=letter;Page=0;return true;
        }
        public bool Erase()
        {if(Composition.Length==0)return false;Composition=Composition.Substring(0,Composition.Length-1);Page=0;return true;}
        public void Clear(){Composition="";Page=0;}
        public void ChangePage(int delta){Page=Math.Max(0,Math.Min(Page+delta,PageCount-1));}
        public string Candidate(int index)
        {int p=Page*PageSize+index;return index<0||index>=PageSize||p>=Scalars.Length?"":Scalars[p];}
        public int FindCandidate(string han)=>Array.IndexOf(Scalars,han);
        public string Select(int index)
        {string value=Candidate(index);if(value.Length>0)Clear();return value;}
    }
}
