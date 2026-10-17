using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public partial class OrderAnnotationsEditor : ObservableObject
{
    private OrderAnnotations? _source;
    private OrderSummary _identity = new(0, null, "", "", "", "", "", null, null, null);
    private string? _confirmedIdentity;
    [ObservableProperty] private bool _hasBending;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommentLengthText)), NotifyPropertyChangedFor(nameof(HasComment))] private string _comment = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCommentError))] private string? _commentError;
    public bool HasCommentError => CommentError is not null;
    public bool HasComment => Comment.Length > 0;
    public string CommentLengthText => $"{Comment.Length}/255";
    public bool NeedsReview => _source is not null && !_source.Matches(_identity) && (HasBending || Comment.Length > 0) &&
        _confirmedIdentity != OrderAnnotations.For(_identity, true, "")!.IdentityHash;
    public void Load(OrderAnnotations? value, OrderSummary identity)
    {
        _source = value; _confirmedIdentity = null; _identity = identity;
        HasBending = value?.HasBending ?? false; Comment = value?.Comment ?? ""; CommentError = null;
        OnPropertyChanged(nameof(NeedsReview));
    }
    public void SetIdentity(DateOnly? date, string specification)
    { _identity = _identity with { OrderDate = date, SpecificationNumber = specification }; OnPropertyChanged(nameof(NeedsReview)); }
    partial void OnHasBendingChanged(bool value) => OnPropertyChanged(nameof(NeedsReview));
    partial void OnCommentChanged(string value) { CommentError = null; OnPropertyChanged(nameof(NeedsReview)); }
    [RelayCommand]
    private void ConfirmReview() { _confirmedIdentity = OrderAnnotations.For(_identity, true, "")!.IdentityHash; OnPropertyChanged(nameof(NeedsReview)); }
    [RelayCommand]
    private void ClearComment() => Comment = "";
    public bool Validate()
    {
        try { OrderAnnotations.ValidateComment(Comment); CommentError = null; return !NeedsReview; }
        catch (OrderWriteException exception) { CommentError = exception.Message; return false; }
    }
    public OrderAnnotations? Build(OrderSummary row)
    {
        SetIdentity(row.OrderDate, row.SpecificationNumber);
        if (!Validate()) throw new ArgumentException(CommentError ?? OrderAnnotations.ReviewMessage);
        return OrderAnnotations.For(row, HasBending, Comment);
    }
}
