using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsvHelper;

namespace wetaca
{
    class Program
    {
        static HttpClient client;

        static void Main(string[] args)
        {
            client = new HttpClient();
         
            MainAsync().GetAwaiter().GetResult();
			Console.WriteLine("Terminado");
		}

        static async Task MainAsync()
        {
			ClearFileIfExists();
			var content = await GetWetacaContent();
            var coursesUrls = CheckAllCourses(content);
            var nutritionalInfos = GetNutritionalInfo(coursesUrls);
            await WriteObjectsToCsvAsync(nutritionalInfos.OrderByDescending(i => i.Properties.Count()));
        }

        static void ClearFileIfExists()
        {
            var path = Directory.GetCurrentDirectory();
			File.Delete(Path.Combine(path, "wetaca.csv"));
		}

        static async Task<string> GetWetacaContent()
        {
            var response = await client.GetAsync("https://wetaca.com/27-nuestros-platos");
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        static IEnumerable<string> CheckAllCourses(string content)
        {
            var matches = Regex.Matches(content, "(data-href=\\\")[^\\\"]*");
            var urls = new List<string>();

            foreach (Match match in matches)
            {
                urls.Add(match.ToString().Split('"')[1]);
            }

            return urls.GroupBy(x => x).Select(x => x.Key);
        }

        static IEnumerable<NutritionalInfo> GetNutritionalInfo(IEnumerable<string> coursesUrls)
        {
            var infos = new List<NutritionalInfo>();
            var tasks = coursesUrls.Select(async u => {
                    var response = await client.GetAsync(u);
                    response.EnsureSuccessStatusCode();

                    var contents = await response.Content.ReadAsStringAsync();
                    infos.Add(GetNutritionalInfoFor(contents));
                });

            Task.WaitAll(tasks.ToArray());

            return infos;
        }
        
        static async Task WriteObjectsToCsvAsync(IEnumerable<NutritionalInfo> objects)
        {
            using (var mem = new MemoryStream())
            using (var writer = new StreamWriter(mem))
            using (var csvWriter = new CsvWriter(writer))
            {
				csvWriter.Configuration.Delimiter = ",";

				await WriteHeaderAsync(csvWriter);
				await WriteRecordsAsync(csvWriter, objects);

				await writer.FlushAsync();
				var result = Encoding.UTF8.GetString(mem.ToArray());

				var path = Directory.GetCurrentDirectory();

				await File.WriteAllTextAsync(Path.Combine(path, "wetaca.csv"), result);
			}
        }

        static async Task WriteHeaderAsync(CsvWriter csvWriter)
        {
            csvWriter.WriteField("Nombre");
            csvWriter.WriteField("Energía (kcal)");
            csvWriter.WriteField("Carbohidratos (gr)");
            csvWriter.WriteField("Grasas totales (gr)");
            csvWriter.WriteField("Azúcares (gr)");
            csvWriter.WriteField("Grasas saturadas (gr)");
            csvWriter.WriteField("Fibra dietética (gr)");
            csvWriter.WriteField("Proteínas (gr)");
            csvWriter.WriteField("Sal (gr)");
			csvWriter.WriteField("Racion (gr)");
			csvWriter.WriteField("Energía Total (kcal)");
			await csvWriter.NextRecordAsync();
        }

        static async Task WriteRecordsAsync(CsvWriter writer, IEnumerable<NutritionalInfo> records)
        {
            foreach (var record in records)
            {
                // Quiero asegurarme que se escriben en el mismo orden de la cabecera
                // y que no escribo la energía en la columna de sal y cosas así
				writer.WriteField(record.Name);

                if (record.Properties.Any())
                {
                    writer.WriteField(record.Properties["Energía"]);
                    writer.WriteField(record.Properties["Carbohidratos"]);
                    writer.WriteField(record.Properties["Grasas totales"]);
                    writer.WriteField(record.Properties["Azúcares"]);
                    writer.WriteField(record.Properties["Grasas saturadas"]);
                    writer.WriteField(record.Properties["Fibra dietética"]);
                    writer.WriteField(record.Properties["Proteínas"]);
                    writer.WriteField(record.Properties["Sal"]);
					writer.WriteField(record.Properties["Racion"]);
					writer.WriteField(record.Properties["EnergiaTotal"]);
				}
				
				await writer.NextRecordAsync();
			}
        }

        static NutritionalInfo GetNutritionalInfoFor(string courseHtml)
        {
            var info = new NutritionalInfo();
            var titleMatch = Regex.Match(courseHtml, "(<h1)[^>]*[^</]*");

            if (titleMatch.Success)
                info.Name = titleMatch.Groups[0].Value.Split(">")[1];

            info.Properties = GetInfoProperties(courseHtml);

            return info;
        }

        static IDictionary<string, double> GetInfoProperties(string courseHtml)
        {
            var result = new Dictionary<string, double>();
            var pattern = "(LC_name\\\"><)[^>]*>([\\wáéíóú\\s]*)|(LC_data\\\")[^>]*>([\\wáéíóú0-9,?\\.?\\s?]*)";
			var courseQuantityPattern = "(Tamaño\\s)(aproximado\\s)?(de la ración\\s)([a-z0-9,\\.\\s]+)";
			var matches = Regex.Matches(courseHtml, pattern);

            if (!matches.Any())
                return result;

            for (var i = 0; i < matches.Count(); i += 2)
            {
                var keyMatch = matches[i];
                var valueMatch = matches[i + 1];

                var key = keyMatch.Groups[0].Value.Split(";\">")[1];
                var value = valueMatch.Groups[0].Value.Split(";\">")[1].Replace(",", ".").Replace("\"", "").Trim();

                try
                {
                    result.Add(key, ToDouble(value));
                }
                catch (Exception ex)
                {
					Console.WriteLine("Error con clave {0} y valor {1}: {2}", key, value, ex.Message);
				}
            }

			var courseSize = Regex.Match(courseHtml, courseQuantityPattern);

            if (courseSize.Success)
				result.Add("Racion", ToDouble(courseSize.Groups[4].Value));
            else
				result.Add("Racion", 0);

			TryToAddTotalEnergy(result, ToDouble(courseSize.Groups[4].Value), result["Energía"]);

			return result;
        }

        private static void TryToAddTotalEnergy(IDictionary<string, double> result, double courseSize, double courseEnergy)
        {
			result.Add("EnergiaTotal", courseEnergy / 100 * courseSize);
		}

        private static double ToDouble(string str)
        {
			return double.Parse(str.Split(" ")[0].Replace(".", ","));
		}

        private class NutritionalInfo
        {
            public string Name { get; set; }
            public IDictionary<string, double> Properties { get; set; }
        }
    }
}
