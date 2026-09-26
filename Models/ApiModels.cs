using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Emby.Plugins.Eporner.Models
{
    // DataContract with explicit names: the API uses snake_case, which Emby's serializer does not map

    [DataContract]
    public class SearchResponse
    {
        [DataMember(Name = "count")] public int Count { get; set; }
        [DataMember(Name = "start")] public int Start { get; set; }
        [DataMember(Name = "per_page")] public int PerPage { get; set; }
        [DataMember(Name = "page")] public int Page { get; set; }
        [DataMember(Name = "total_count")] public int TotalCount { get; set; }
        [DataMember(Name = "total_pages")] public int TotalPages { get; set; }
        [DataMember(Name = "videos")] public List<EpVideo> Videos { get; set; } = new List<EpVideo>();
    }

    [DataContract]
    public class EpVideo
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "keywords")] public string Keywords { get; set; }
        [DataMember(Name = "views")] public long Views { get; set; }
        [DataMember(Name = "rate")] public string Rate { get; set; }
        [DataMember(Name = "url")] public string Url { get; set; }
        [DataMember(Name = "added")] public string Added { get; set; }
        [DataMember(Name = "length_sec")] public int LengthSec { get; set; }
        [DataMember(Name = "length_min")] public string LengthMin { get; set; }
        [DataMember(Name = "embed")] public string Embed { get; set; }
        [DataMember(Name = "default_thumb")] public EpThumb DefaultThumb { get; set; }
        [DataMember(Name = "thumbs")] public List<EpThumb> Thumbs { get; set; }
    }

    [DataContract]
    public class EpThumb
    {
        [DataMember(Name = "size")] public string Size { get; set; }
        [DataMember(Name = "width")] public int Width { get; set; }
        [DataMember(Name = "height")] public int Height { get; set; }
        [DataMember(Name = "src")] public string Src { get; set; }
    }

    [DataContract]
    public class XhrVideoResponse
    {
        [DataMember(Name = "available")] public bool? Available { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "sources")] public Dictionary<string, Dictionary<string, XhrSource>> Sources { get; set; }
    }

    [DataContract]
    public class XhrSource
    {
        [DataMember(Name = "src")] public string Src { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "labelShort")] public string LabelShort { get; set; }
        [DataMember(Name = "size")] public string Size { get; set; }
    }

    public class StreamInfo
    {
        public string Url { get; set; }
        public string Container { get; set; }
        public int Height { get; set; }
        public string Label { get; set; }
    }

    public class EpCategory
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
    }
}
