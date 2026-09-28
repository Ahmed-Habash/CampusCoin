using System.Globalization;
using System.Text;
using CampusCoin.Models;

namespace CampusCoin.Services;

public sealed class TransactionPdfService
{
    public byte[] Create(IReadOnlyList<Transaction> rows, CurrencyService currency)
    {
        var pages = new List<List<string>>();
        var current = new List<string>();
        foreach (var row in rows)
        {
            var sign = row.Category.Type == CategoryType.Income ? "+" : "-";
            current.Add($"{row.Date:dd MMM yyyy}|{Trim(row.Description, 31)}|{Trim(row.Category.Name, 18)}|{row.Category.Type}|{sign}{currency.Format(row.AmountCents)}");
            if (current.Count == 30) { pages.Add(current); current = []; }
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(current);

        var objects = new List<byte[]>();
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageIds = Enumerable.Range(0, pages.Count).Select(i => 4 + i * 2).ToArray();
        objects.Add(Ascii($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        for (var i = 0; i < pages.Count; i++)
        {
            var pageId = pageIds[i]; var contentId = pageId + 1;
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));
            var content = BuildPage(pages[i], i + 1, pages.Count, currency.Code);
            objects.Add(Ascii($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"));
        }
        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n%CC01\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write(output, $"{i + 1} 0 obj\n"); output.Write(objects[i]); Write(output, "\nendobj\n"); }
        var xref = output.Position;
        Write(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(output, $"{offset:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static string BuildPage(IReadOnlyList<string> rows, int page, int totalPages, string currency)
    {
        var b = new StringBuilder("BT\n/F1 20 Tf\n45 795 Td\n(Campus Coin - Financial activity) Tj\n/F1 9 Tf\n0 -20 Td\n");
        b.Append($"(Generated {DateTime.Today:dd MMM yyyy}  |  Currency: {Escape(currency)}  |  Page {page} of {totalPages}) Tj\n0 -30 Td\n/F1 8 Tf\n");
        b.Append("(DATE                 DESCRIPTION                         CATEGORY                 TYPE          AMOUNT) Tj\n0 -8 Td\n(________________________________________________________________________________________) Tj\n");
        foreach (var row in rows)
        {
            var cells = row.Split('|');
            b.Append($"0 -19 Td\n({Escape(cells[0])}) Tj\n85 0 Td\n({Escape(cells[1])}) Tj\n190 0 Td\n({Escape(cells[2])}) Tj\n105 0 Td\n({Escape(cells[3])}) Tj\n65 0 Td\n({Escape(cells[4])}) Tj\n-445 0 Td\n");
        }
        b.Append("ET"); return b.ToString();
    }
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("…", "...");
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static void Write(Stream stream, string value) { var bytes = Ascii(value); stream.Write(bytes); }
}
