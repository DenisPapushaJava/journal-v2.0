using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using System.Diagnostics.CodeAnalysis;

namespace Jornal.Desktop.ViewModels;

public partial class OrderAnnotationsEditor : ObservableObject
{
    private OrderAnnotations? _source;
    private OrderOperations? _operationsSource;
    private string? _confirmedOperationsIdentity;
    [ObservableProperty] private bool _hasChamfer;
    private OrderSummary _identity = new(0, null, "", "", "", "", "", null, null, null);
    private string? _confirmedIdentity;
    [ObservableProperty] private bool _hasBending;
    private string _comment = "";
    [AllowNull]
    public string Comment
    {
        get => _comment;
        set
        {
            var text = value ?? "";
            if (_comment == text) return;
            OnPropertyChanging(nameof(Comment));
            _comment = text;
            OnCommentChanged(text);
            OnPropertyChanged(nameof(Comment));
            OnPropertyChanged(nameof(CommentLengthText));
            OnPropertyChanged(nameof(HasComment));
        }
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCommentError))] private string? _commentError;
    public bool HasCommentError => CommentError is not null;
    public bool HasComment => Comment.Length > 0;
    public string CommentLengthText => $"{Comment.Length}/255";
    private bool AnnotationsNeedReview => _source is not null && !_source.Matches(_identity) && (HasBending || Comment.Length > 0) &&
        _confirmedIdentity != OrderAnnotations.For(_identity, true, "")!.IdentityHash;
    public bool NeedsReview => AnnotationsNeedReview || _operationsSource is not null && !_operationsSource.Matches(_identity) && HasChamfer &&
        _confirmedOperationsIdentity != OrderOperations.For(_identity, true)!.IdentityHash;
    public void Load(OrderAnnotations? value, OrderSummary identity)
    {
        _source = value; _confirmedIdentity = null; _identity = identity;
        _operationsSource = identity.Operations; _confirmedOperationsIdentity = null; HasChamfer = identity.Operations?.HasChamfer ?? false;
        HasBending = value?.HasBending ?? false; Comment = value?.Comment ?? ""; CommentError = null;
        OnPropertyChanged(nameof(NeedsReview));
    }
    public void SetIdentity(DateOnly? date, string specification)
    { _identity = _identity with { OrderDate = date, SpecificationNumber = specification }; OnPropertyChanged(nameof(NeedsReview)); }
    partial void OnHasChamferChanged(bool value) => OnPropertyChanged(nameof(NeedsReview));
    partial void OnHasBendingChanged(bool value) => OnPropertyChanged(nameof(NeedsReview));
    private void OnCommentChanged(string value) { CommentError = null; OnPropertyChanged(nameof(NeedsReview)); }
    [RelayCommand]
    private void ConfirmReview() { _confirmedIdentity = OrderAnnotations.For(_identity, true, "")!.IdentityHash; _confirmedOperationsIdentity = OrderOperations.For(_identity, true)!.IdentityHash; OnPropertyChanged(nameof(NeedsReview)); }
    [RelayCommand]
    private void ClearComment() => Comment = "";
    public bool Validate()
    {
        try { OrderAnnotations.ValidateComment(Comment); CommentError = null; return !NeedsReview; }
        catch (OrderWriteException exception) { CommentError = exception.Message; return false; }
    }
    public OrderOperations? BuildOperations(OrderSummary row)
    {
        SetIdentity(row.OrderDate, row.SpecificationNumber);
        if (!Validate()) throw new ArgumentException(CommentError ?? "Проверьте дополнительные операции и комментарий.");
        return OrderOperations.For(row, HasChamfer);
    }
    public OrderAnnotations? Build(OrderSummary row)
    {
        SetIdentity(row.OrderDate, row.SpecificationNumber);
        if (!Validate()) throw new ArgumentException(CommentError ?? "Проверьте гибку, фаску и комментарий.");
        return OrderAnnotations.For(row, HasBending, Comment);
    }
}
