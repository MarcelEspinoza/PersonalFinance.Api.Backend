namespace PersonalFinance.Api.Models.Dtos.Import
{
    public class ImportResult
    {
        public List<object> Imported { get; set; } = new();
        public List<object> Pending { get; set; } = new();
    }

}
