using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebApiZadatak.Models;

namespace WebApiZadatak.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiUrl;

        public ReportController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _apiUrl = configuration["TimeEntriesApiUrl"] ??
                "https://rc-vault-fap-live-1.azurewebsites.net/api/gettimeentries?code=vO17RnE8vuzXzPJo5eaLLjXjmRW07law99QTD90zat9FfOQJKKUcgQ==";
        }

        [HttpGet("html-report")]
        public async Task<ActionResult> GetHtmlReport()
        {
            var employeeHours = await GetEmployeeHoursAsync();
            if (employeeHours == null)
                return StatusCode(500, "Failed to retrieve or process time entries.");

            var html = new StringBuilder();
            html.Append("<html><head><style>");
            html.Append("table { border-collapse: collapse; width: 60%; margin: 20px auto; }");
            html.Append("th, td { border: 1px solid #ccc; padding: 8px; text-align: left; }");
            html.Append("tr.low-hours { background-color: #ffcccc; }");
            html.Append("</style></head><body>");
            html.Append("<h2 style='text-align:center'>Employee Work Hours</h2>");
            html.Append("<table><tr><th>Name</th><th>Total Hours</th></tr>");

            foreach (var emp in employeeHours)
            {
                string rowClass = emp.TotalHours < 100 ? " class='low-hours'" : "";
                html.Append($"<tr{rowClass}><td>{emp.Name}</td><td>{emp.TotalHours:F2}</td></tr>");
            }

            html.Append("</table>");
            html.Append("<h2>Employee Work Distribution (Pie Chart)</h2>");
            html.Append("<img src=\"/report/piechart\" alt=\"Pie Chart\" />");
            html.Append("</body></html>");

            return Content(html.ToString(), "text/html");
        }

        [HttpGet("piechart")]
        public async Task<ActionResult> GetPieChart()
        {
            var employeeHours = await GetEmployeeHoursAsync();
            if (employeeHours == null || !employeeHours.Any())
                return StatusCode(500, "Failed to retrieve or process time entries.");

            int width = 600, height = 600;
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.White);

            float totalHours = (float)employeeHours.Sum(e => e.TotalHours);
            float startAngle = 0f;
            var random = new Random();
            var font = new Font("Arial", 10);

            for (int i = 0; i < employeeHours.Count; i++)
            {
                var emp = employeeHours[i];
                float sweepAngle = (float)(emp.TotalHours / totalHours) * 360f;
                var brush = new SolidBrush(Color.FromArgb(random.Next(50, 255), random.Next(50, 255), random.Next(50, 255)));
                graphics.FillPie(brush, 100, 100, 400, 400, startAngle, sweepAngle);

                var label = $"{emp.Name} ({(emp.TotalHours / totalHours * 100):F1}%)";
                graphics.DrawString(label, font, Brushes.Black, 20, 20 + i * 20);

                startAngle += sweepAngle;
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);

            return File(ms.ToArray(), "image/png");
        }

        private async Task<List<EmployeeHour>?> GetEmployeeHoursAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync(_apiUrl);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var timeEntries = JsonSerializer.Deserialize<List<TimeEntry>>(json, options);

                if (timeEntries == null)
                    return null;

                return timeEntries
                    .GroupBy(e => e.EmployeeName)
                    .Select(g => new EmployeeHour
                    {
                        Name = g.Key,
                        TotalHours = g.Sum(e => (e.TimeOut - e.TimeIn).TotalHours)
                    })
                    .OrderByDescending(x => x.TotalHours)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        private class EmployeeHour
        {
            public string Name { get; set; } = string.Empty;
            public double TotalHours { get; set; }
        }
    }
}