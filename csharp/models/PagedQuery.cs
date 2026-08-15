namespace Dotfiles.Models;

public class PagedQuery
{
    public PagedQuery()
    {
    }

    public PagedQuery(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}