using System;
using System.Net;
using System.Threading;
using System.Net.Http;
using System.IO;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace sistemsko_projekat_1
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Pokrenut server na:  http://localhost:8080/");
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");
            listener.Start();
            // Thread-safe key-value kolekcija 
            ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();
            HttpClient httpClient = new HttpClient();

            while (true)
            {
                var context = listener.GetContext();
                ThreadPool.QueueUserWorkItem(o => HandleRequest(context, cache, httpClient));
            }
        }

        private static void HandleRequest(HttpListenerContext context, ConcurrentDictionary<string, string> cache, HttpClient httpClient)
        {
            // Dobijamo url bez http://localhost:8080/
            // i u slucaju loseg enpointa vracamo gresku sa codom 404 not found
            Console.WriteLine("Novi request: " + context.Request.RawUrl);
            string rawUrl = context.Request.RawUrl ?? "";
            if (!rawUrl.StartsWith("/search?q="))
            {
                SendResponse(context, "Los endpoint. Koristi /search?q=", 404);
                Console.WriteLine("[NEUSPESNO] : Los endpoint");
                return;
            }

            // Provera za prazan upit
            string query = WebUtility.UrlDecode(rawUrl.Substring("/search?q=".Length));
            if (query == null || query == "")
            {
                SendResponse(context, "Prazan upit.", 400);
                Console.WriteLine("[NEUSPESNO] : Prazan upit");
                return;
            }

            // Provera u ConcurentDictionary da li je taj zahtev vec bio kesiran
            string cacheKey = query.ToLowerInvariant();
            if (cache.TryGetValue(cacheKey, out string cached))
            {
                SendResponse(context, cached);
                Console.WriteLine("[USPESNO] : prethodno obradjeni upit iz memorije");
             
                return;
            }

            try
            {
                // API
                string apiUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/search?q={query}";
                var response = httpClient.GetAsync(apiUrl).Result;
                string content = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    SendResponse(context, "Neuspesno pribavljanje podataka.", 500);
                    Console.WriteLine("[NEUSPESNO] : Neuspesno pribavljanje podataka");
                    return;
                }

                JObject json = JObject.Parse(content);
                if ((int)json.First.Last == 0)
                {
                    SendResponse(context, $"Nema rezultata za:  '{query}'.");
                    Console.WriteLine("[USPESNO] : Nema rezultata za query");
                    return;
                }
                JArray ids = (JArray)json["objectIDs"] ?? new JArray();
                if (ids.Count == 0)
                {
                    SendResponse(context, $"Nema rezultata za:  '{query}'.");
                    Console.WriteLine("[USPESNO] : Nema rezultata za query");
                    return;
                }


                string result = $"Pronadjeno {ids.Count} instanci za: '{query}':\n";
                int shown = 0;
                foreach (var id in ids)
                {
                    if (shown >= 5) break;
                    string detailsUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";
                    var detailResp = httpClient.GetAsync(detailsUrl).Result;
                    if (!detailResp.IsSuccessStatusCode) continue;
                    string detailJson = detailResp.Content.ReadAsStringAsync().Result;
                    JObject obj = JObject.Parse(detailJson);
                    string title = (string)obj["title"] ?? "Nepoznati naziv";
                    string artist = (string)obj["artistDisplayName"] ?? "Nepoznati umetnik";
                    result += $"{title} - {artist}\n";
                    shown++;
                }

                cache[cacheKey] = result;
                SendResponse(context, result);
                Console.WriteLine("[USPESNO] : Bez problema");
            }
            catch(Exception ex)
            {
                SendResponse(context, $"Greska: {ex.Message}" , 500);
                Console.WriteLine("[NEUSPESNO] : " + ex.Message);
            }
        }

        private static void SendResponse(HttpListenerContext context, string message, int statusCode = 200)
        {
            context.Response.StatusCode = statusCode;
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                writer.Write(message);
            }
        }
    }
}
