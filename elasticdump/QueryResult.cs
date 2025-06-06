using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace elasticdump;

public record Hit
{
    public required JsonElement _source { get; set; }
    public required List<JsonElement> sort { get; set; }

    //public SortElement Sort()
    //{
    //    return new SortElement()
    //    {
    //        Timestamp = sort[0].GetDecimal(),
    //        DocId = sort[1].ToString()
    //    };
    //}
}

public record Hits
{

    public required List<Hit> hits { get; set; }
}
public record QueryResult
{

    public required Hits hits { get; set; }
}

