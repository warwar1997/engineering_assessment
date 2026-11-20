namespace Rezerv.Domain.Entities;

public class Instructor
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}
