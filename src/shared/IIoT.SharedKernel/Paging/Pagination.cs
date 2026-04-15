namespace IIoT.SharedKernel.Paging;

public class Pagination
{
    private const int MaxPageSize = 100;
    private int _pageNumber = 1;
    private int _pageSize = 10;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            var normalized = value < 1 ? 1 : value;
            _pageSize = normalized > MaxPageSize ? MaxPageSize : normalized;
        }
    }
}
