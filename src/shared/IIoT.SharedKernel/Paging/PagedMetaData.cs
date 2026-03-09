namespace IIoT.SharedKernel.Paging;

public class PagedMetaData
{
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }

    // 上一页
    public bool HasPrevious => CurrentPage > 1;

    // 下一页
    public bool HasNext => CurrentPage < TotalPages;
}
