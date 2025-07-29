using System;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace sistemsko_projekat_2
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Pokrenut server2 na:  http://localhost:8080/");
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");
            listener.Start();

            ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();
            HttpClient httpClient = new HttpClient();

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, cache, httpClient));
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext context, ConcurrentDictionary<string, string> cache, HttpClient httpClient)
        {
            Console.WriteLine($"[NOVI ZAHTEV] {context.Request.RawUrl}");

            string rawUrl = context.Request.RawUrl ?? "";
            if (!rawUrl.StartsWith("/search?q="))
            {
                Console.WriteLine("[NEUSPESNO] : Los endpoint");
                await SendResponseAsync(context, "Los endpoint. Koristi /search?q=", 404);
                return;
            }

            string query = WebUtility.UrlDecode(rawUrl.Substring("/search?q=".Length));
            if (string.IsNullOrEmpty(query))
            {
                Console.WriteLine("[NEUSPESNO] : Prazan upit");
                await SendResponseAsync(context, "Prazan upit.", 400);
                return;
            }

            string cacheKey = query.ToLowerInvariant();
            if (cache.TryGetValue(cacheKey, out string cached))
            {
                Console.WriteLine("[USPESNO] : Rezultat iz keša");
                await SendResponseAsync(context, cached);
                return;
            }

            Console.WriteLine("[INFO] : Slanje zahteva ka MET API");

            string apiUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/search?q={query}";
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(apiUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NEUSPESNO] : Greška pri pozivu API-ja: {ex.Message}");
                await SendResponseAsync(context, $"Greška pri pozivu API-ja: {ex.Message}", 500);
                return;
            }

            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("[NEUSPESNO] : API vratio neuspešan status");
                await SendResponseAsync(context, "Neuspešno pribavljanje podataka.", 500);
                return;
            }

            JObject json = JObject.Parse(content);
            if ((int)json.First.Last == 0)
            {
                await SendResponseAsync(context, $"Nema rezultata za:  '{query}'.");
                Console.WriteLine("[USPESNO] : Nema rezultata za query");
                return;
            }
            JArray ids = (JArray)json["objectIDs"] ?? new JArray();
            if (ids.Count == 0)
            {
                await SendResponseAsync(context, $"Nema rezultata za:  '{query}'.");
                Console.WriteLine("[USPESNO] : Nema rezultata za query");
                return;
            }

            Console.WriteLine($"[INFO] : Pronadjeno {ids.Count} rezultata, preuzimam prvih 5 asinhrono");

            string result = $"Pronadjeno {ids.Count} instanci za: '{query}':\n";

            // Paralelna obrada prvih 5 ID-jeva
            var tasks = new List<Task<string>>();
            foreach (var id in ids.Take(5))
            {
                tasks.Add(GetArtworkDetailsAsync(httpClient, id.ToString()));
            }

            var detailsResults = await Task.WhenAll(tasks);
            int validCount = 0;
            foreach (var detail in detailsResults)
            {
                if (!string.IsNullOrEmpty(detail))
                {
                    result += detail + "\n";
                    validCount++;
                }
            }

            Console.WriteLine($"[USPESNO] : Obradjeno {validCount} umetničkih dela");

            cache[cacheKey] = result;
            await SendResponseAsync(context, result);
        }

        private static async Task<string> GetArtworkDetailsAsync(HttpClient httpClient, string id)
        {
            try
            {
                string detailsUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";
                var detailResp = await httpClient.GetAsync(detailsUrl);
                if (!detailResp.IsSuccessStatusCode) return string.Empty;

                string detailJson = await detailResp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(detailJson);
                string title = (string)obj["title"] ?? "Nepoznati naziv";
                string artist = (string)obj["artistDisplayName"] ?? "Nepoznati umetnik";
                return $"{title} - {artist}";
            }
            catch(Exception ex)
            {
                Console.WriteLine("[NEUSPESNO] : "+ ex.Message);
                return ex.Message;
            }
        }

        private static async Task SendResponseAsync(HttpListenerContext context, string message, int statusCode = 200)
        {
            context.Response.StatusCode = statusCode;
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                await writer.WriteAsync(message);
            }
        }
    }
}
