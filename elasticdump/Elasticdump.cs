using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace elasticdump;

public class ElasticDump
{
    private HttpClient _HttpClient;
    private string DestinationDirectory;
    private StringBuilder ToCompress = new StringBuilder();
    private int PartitionNumDocs = 1000000;

    /// <summary>
    /// ElasticDump Constructor
    /// </summary>
    /// <param name="destinationDirectory">Pasta onde serão salvos os logs </param>
    public ElasticDump(string url, string username, string password, string destinationDirectory)
    {
        string? bp = Path.GetFullPath(destinationDirectory);
        if (bp == null)
        {
            throw new ArgumentException("Argumento destinationDirectory é obrigatório");
        }
        DestinationDirectory = bp;

        _HttpClient = new HttpClient();
        _HttpClient.BaseAddress = new Uri(url);

        _HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes($"{username}:{password}"))
        );

    }

    /// <summary>
    /// Método <c>GetContent</c> utiliza a API _search do elasticsearch para 
    /// </summary>
    /// <param name="index">Nome do "indice" do Elasticsearch</param>
    /// <param name="size">Quantidades de documentos(logs) que a requisição deve retornar</param>
    /// <param name="lastId">Parâmetro utilizado na paginação, campo _id do documento</param>
    /// <param name="lastTimestamp">Parâmetro utilizado na paginação, campo @timestamp do documento</param>
    /// <returns>Retorna um Strem para a String com JSON da resposta do Elasticsearch (contém mais informações do que os documentos)</returns>
    private async Task<Stream> GetContent(string index, int size, decimal? lastTimestamp = null)
    {
        string query = $@"
        {{
            ""size"": {size},
            ""sort"": [
                {{
                    ""@timestamp"": ""asc""
                }}
            ]
        ";

        if (lastTimestamp != null)
        {
            query += $@"
                ,""search_after"": [
                    {lastTimestamp}
                ]
            }}
            ";
        }
        else
        {
            query += "}";
        }
        var request = new HttpRequestMessage(HttpMethod.Get, $"{index}/_search");
        request.Content = new StringContent(query, Encoding.UTF8, "application/json");
        var response = await _HttpClient.SendAsync(request);
        //var responseContent = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Método ProcessResults transforma json no objeto do tipo QueryResult
    /// </summary>
    /// <param name="content"></param>
    /// <returns>Retorna uma QueryResult</returns>
    private async Task<QueryResult?> ProcessResults(Stream content)
    {
        return await JsonSerializer.DeserializeAsync<QueryResult>(content);
    }

    /// <summary>
    /// Método <c>Compress</c> recebe uma lista de resultados (com campo _source),
    /// transforma em um texto com uma linha para cada documento (log) e comprime
    /// </summary>
    /// <param name="compressor">Objecto que faz a compressão</param>
    /// <param name="hits">Resultado da query do Elasticsearch</param>
    private async Task Compress(GZipStream compressor, List<Hit>? hits)
    {
        if (hits == null || hits.Count == 0)
        {
            return;
        }

        ToCompress.Clear();
        foreach (var hit in hits)
        {
            ToCompress.AppendLine(hit._source.ToString());
        }
        await compressor.WriteAsync(Encoding.UTF8.GetBytes(ToCompress.ToString()));
    }

    private async Task<JsonElement> GetIndexPropertiesAsync(string index)
    {
        var p = await _HttpClient.GetFromJsonAsync<JsonElement>($"{index}");
        return p.GetProperty(index);
    }

    private async Task<int> GetNumberOfDocuments(string index)
    {
        var p = await _HttpClient.GetFromJsonAsync<JsonElement>($"{index}/_stats");
        return p.GetProperty("_all").GetProperty("primaries").GetProperty("docs").GetProperty("count").GetInt32();
    }

    /// <summary>
    /// Método Dump busca os logs de um índice do Elasticsearch,
    /// salva o resultado no formato JSON em arquivos compactados GZIP.
    /// </summary>
    /// <param name="index"></param>
    /// <param name="rowPerRequest"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task Dump(string index, int rowPerRequest)
    {
        int partitionNumber = 1;
        FileStream compressedFileStream = File.Create(
            Path.Combine(DestinationDirectory, $"{index}.split-{partitionNumber:D6}.json.gz")
        );
        GZipStream? compressor = new GZipStream(compressedFileStream, CompressionMode.Compress);
        if (compressor == null)
        {
            throw new Exception("GZipStream retornou null");
        }

        // Cria arquivo settings no formato json e outro arquivo com mappings.
        var indexProperties = await GetIndexPropertiesAsync(index);
        using (StreamWriter writer = new StreamWriter(Path.Combine(DestinationDirectory, $"{index}-settings.json")))
        {
            writer.Write(indexProperties.GetProperty("settings").GetRawText());
        }
        using (StreamWriter writer = new StreamWriter(Path.Combine(DestinationDirectory, $"{index}-mappings.json")))
        {
            writer.Write(indexProperties.GetProperty("mappings").GetRawText());
        }

        int indexNumberOfDocuments = await GetNumberOfDocuments(index);

        int totalRows = 0;
        int partitionRows = 0;

        Stream content = await GetContent(
            index: index,
            size: rowPerRequest
        );

        QueryResult? queryResult = await ProcessResults(content);

        if (queryResult == null)
        {
            throw new Exception("ProcessResults retornou null");
        }

        int rowCount = queryResult.hits.hits.Count;

        Task compressTask;
        Task<Stream> getContentTask;

        do
        {
            totalRows += rowCount;
            partitionRows += rowCount;

            Console.WriteLine($"{totalRows} de {indexNumberOfDocuments}. {100.0 * totalRows / indexNumberOfDocuments:F2}%");
            Console.WriteLine();

            compressTask = Compress(compressor, queryResult.hits.hits);

            var lastDoc = queryResult.hits.hits.Last();


            getContentTask = GetContent(
                index: index,
                size: rowPerRequest,
                lastTimestamp: lastDoc.sort[0].GetDecimal()
            );

            Task.WaitAll(getContentTask, compressTask);

            if (partitionRows >= PartitionNumDocs)
            {
                compressor.Close();
                compressedFileStream.Close();
                partitionRows = 0;
                partitionNumber++;

                compressedFileStream = File.Create(
                    Path.Combine(DestinationDirectory, $"{index}.split-{partitionNumber:D6}.json.gz")
                );
                compressor = new GZipStream(compressedFileStream, CompressionMode.Compress);
                if (compressor == null)
                {
                    throw new Exception("GZipStream retornou null");
                }
            }

            queryResult = await ProcessResults(getContentTask.Result);
            if (queryResult == null)
            {
                throw new Exception("ProcessResults retornou null");
            }
            rowCount = queryResult.hits.hits.Count;



        } while (rowCount > 0);

        compressor.Close();
        compressedFileStream.Close();
    }
}
