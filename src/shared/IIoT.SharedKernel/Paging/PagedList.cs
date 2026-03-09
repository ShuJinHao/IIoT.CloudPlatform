namespace IIoT.SharedKernel.Paging;

public class PagedList<T> : List<T>
{
    public PagedList(IEnumerable<T> items, int count, Pagination pagination)
    {
        MetaData = new PagedMetaData
        {
            TotalCount = count,
            PageSize = pagination.PageSize,
            CurrentPage = pagination.PageNumber,
            TotalPages = (int)Math.Ceiling(count / (double)pagination.PageSize)
        };

        AddRange(items);
    }

    public PagedMetaData MetaData { get; set; }
}