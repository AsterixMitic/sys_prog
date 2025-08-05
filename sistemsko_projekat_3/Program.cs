using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/*
Zadatak 17:
Koristeći principe Reaktivnog programiranja i TheCoctailDB API-a, implementirati aplikaciju za
prikaz instrukcija za pripremu koktela (strInstructions property). Koristiti opciju za pretragu
koktela po imenu (search cocktail by name). Prilikom poziva proslediti odgovarajuće ime koktela.
Za prikupljene instrukcije odrediti Word Cloud, odnosno broj pojavljivanja svake reči. Prikazati
dobijene rezultate.
Dokumentacija dostupna na linku: https://www.thecocktaildb.com/api.php
 */

namespace sistemsko_projekat_cocktail
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Write("Unesi ime koktela: ");
            string cocktailName = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(cocktailName))
            {
                Console.WriteLine("Nije uneto ime koktela.");
                return;
            }

            try
            {
                string instructions = await GetCocktailInstructionsAsync(cocktailName);

                if (string.IsNullOrEmpty(instructions))
                {
                    Console.WriteLine("Nema rezultata za dati koktel.");
                    return;
                }

                Console.WriteLine("\nInstrukcije za pripremu:");
                Console.WriteLine(instructions);

                Console.WriteLine("\nWord Cloud (učestalost reči):");
                Dictionary<string, int> wordCloud = GenerateWordCloud(instructions);

                foreach (var item in wordCloud.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"{item.Key} - {item.Value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška: {ex.Message}");
            }
            finally
            {
            Console.ReadLine();
            }
        }

        static async Task<string> GetCocktailInstructionsAsync(string cocktailName)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                string url = $"https://www.thecocktaildb.com/api/json/v1/1/search.php?s={cocktailName}";

                HttpResponseMessage response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                string json = await response.Content.ReadAsStringAsync();
                JObject data = JObject.Parse(json);

                JToken drink = data["drinks"]?.FirstOrDefault();
                if (drink == null) return string.Empty;

                return (string)drink["strInstructions"] ?? string.Empty;
            }
        }

        static Dictionary<string, int> GenerateWordCloud(string text)
        {
            var words = text
                .ToLower()
                .Split(new[] { ' ', '\r', '\n', '.', ',', ';', ':', '-', '!' }, StringSplitOptions.RemoveEmptyEntries);

            Dictionary<string, int> frequency = new Dictionary<string, int>();

            foreach (string word in words)
            {
                if (frequency.ContainsKey(word))
                    frequency[word]++;
                else
                    frequency[word] = 1;
            }

            return frequency;
        }
    }
}
