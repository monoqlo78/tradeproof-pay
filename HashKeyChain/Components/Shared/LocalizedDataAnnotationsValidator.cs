using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;

namespace HashKeyChain.Components.Shared;

/// <summary>
/// Drop-in replacement for <c>DataAnnotationsValidator</c> that resolves each
/// attribute's ErrorMessage (used as a resource key) through the shared
/// localizer, so validation messages honour the current UI culture inside an
/// <see cref="EditForm"/>. Because Blazor Server runs validation on the server
/// over the circuit, this covers both the immediate (per-field) and submit paths.
/// </summary>
public sealed class LocalizedDataAnnotationsValidator : ComponentBase, IDisposable
{
    private ValidationMessageStore _messages = default!;
    private EditContext? _editContext;

    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (CurrentEditContext is null)
        {
            throw new InvalidOperationException(
                $"{nameof(LocalizedDataAnnotationsValidator)} requires a cascading EditContext. " +
                "It must be placed inside an EditForm.");
        }

        _editContext = CurrentEditContext;
        _messages = new ValidationMessageStore(_editContext);
        _editContext.OnValidationRequested += HandleValidationRequested;
        _editContext.OnFieldChanged += HandleFieldChanged;
    }

    private void HandleValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _messages.Clear();
        foreach (var prop in _editContext!.Model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            ValidateProperty(_editContext.Model, prop);
        }

        _editContext.NotifyValidationStateChanged();
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        var field = e.FieldIdentifier;
        _messages.Clear(field);

        var prop = field.Model.GetType().GetProperty(field.FieldName);
        if (prop is not null)
        {
            ValidateProperty(field.Model, prop);
        }

        _editContext!.NotifyValidationStateChanged();
    }

    private void ValidateProperty(object model, PropertyInfo prop)
    {
        var value = prop.GetValue(model);
        var field = new FieldIdentifier(model, prop.Name);

        foreach (var attr in prop.GetCustomAttributes<ValidationAttribute>(inherit: true))
        {
            if (!attr.IsValid(value))
            {
                _messages.Add(field, BuildMessage(attr, prop.Name));
            }
        }
    }

    private string BuildMessage(ValidationAttribute attr, string propertyName)
    {
        if (string.IsNullOrEmpty(attr.ErrorMessage))
        {
            return attr.FormatErrorMessage(propertyName);
        }

        var template = Localizer[attr.ErrorMessage];
        if (template.ResourceNotFound)
        {
            return attr.FormatErrorMessage(propertyName);
        }

        // {0} = member name, remaining args match the standard DataAnnotations order.
        object?[] args = attr switch
        {
            StringLengthAttribute s => new object?[] { propertyName, s.MaximumLength, s.MinimumLength },
            RangeAttribute r => new object?[] { propertyName, r.Minimum, r.Maximum },
            MinLengthAttribute m => new object?[] { propertyName, m.Length },
            MaxLengthAttribute m => new object?[] { propertyName, m.Length },
            _ => new object?[] { propertyName }
        };

        try
        {
            return string.Format(template.Value, args);
        }
        catch (FormatException)
        {
            return template.Value;
        }
    }

    public void Dispose()
    {
        if (_editContext is not null)
        {
            _editContext.OnValidationRequested -= HandleValidationRequested;
            _editContext.OnFieldChanged -= HandleFieldChanged;
        }
    }
}
