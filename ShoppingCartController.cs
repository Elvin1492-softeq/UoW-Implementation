namespace Nop.Web.Controllers
{
    [AutoValidateAntiforgeryToken]
    public partial class ShoppingCartController : BasePublicController
    {
        #region Fields

        #endregion

        #region Ctor

        public ShoppingCartController()
        {

        }

        #endregion

        #region Utilities

        protected virtual async Task ParseAndSaveCheckoutAttributesAsync(IList<ShoppingCartItem> cart, IFormCollection form)
        {
            if (cart == null)
                throw new ArgumentNullException(nameof(cart));

            if (form == null)
                throw new ArgumentNullException(nameof(form));

            var attributesXml = string.Empty;
            var excludeShippableAttributes = !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);
            var store = await _storeContext.GetCurrentStoreAsync();
            var checkoutAttributes = await _checkoutAttributeService.GetAllCheckoutAttributesAsync(store.Id, excludeShippableAttributes);

            using (var trx = _unitOfWork.BeginTransaction())
            {
                try
                {
                    foreach (var attribute in checkoutAttributes)
                    {
                        var controlId = $"checkout_attribute_{attribute.Id}";
                        switch (attribute.AttributeControlType)
                        {
                            case AttributeControlType.DropdownList:
                            case AttributeControlType.RadioList:
                            case AttributeControlType.ColorSquares:
                            case AttributeControlType.ImageSquares:
                                {
                                    var ctrlAttributes = form[controlId];
                                    if (!StringValues.IsNullOrEmpty(ctrlAttributes))
                                    {
                                        var selectedAttributeId = int.Parse(ctrlAttributes);
                                        if (selectedAttributeId > 0)
                                            attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                                attribute, selectedAttributeId.ToString());
                                    }
                                }

                                break;
                            case AttributeControlType.Checkboxes:
                                {
                                    var cblAttributes = form[controlId];
                                    if (!StringValues.IsNullOrEmpty(cblAttributes))
                                    {
                                        foreach (var item in cblAttributes.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            var selectedAttributeId = int.Parse(item);
                                            if (selectedAttributeId > 0)
                                                attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                                    attribute, selectedAttributeId.ToString());
                                        }
                                    }
                                }

                                break;
                            case AttributeControlType.ReadonlyCheckboxes:
                                {
                                    //load read-only (already server-side selected) values
                                    var attributeValues = await _checkoutAttributeService.GetCheckoutAttributeValuesAsync(attribute.Id);
                                    foreach (var selectedAttributeId in attributeValues
                                        .Where(v => v.IsPreSelected)
                                        .Select(v => v.Id)
                                        .ToList())
                                    {
                                        attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                                    attribute, selectedAttributeId.ToString());
                                    }
                                }

                                break;
                            case AttributeControlType.TextBox:
                            case AttributeControlType.MultilineTextbox:
                                {
                                    var ctrlAttributes = form[controlId];
                                    if (!StringValues.IsNullOrEmpty(ctrlAttributes))
                                    {
                                        var enteredText = ctrlAttributes.ToString().Trim();
                                        attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                            attribute, enteredText);
                                    }
                                }

                                break;
                            case AttributeControlType.Datepicker:
                                {
                                    var date = form[controlId + "_day"];
                                    var month = form[controlId + "_month"];
                                    var year = form[controlId + "_year"];
                                    DateTime? selectedDate = null;
                                    try
                                    {
                                        selectedDate = new DateTime(int.Parse(year), int.Parse(month), int.Parse(date));
                                    }
                                    catch
                                    {
                                        // ignored
                                    }

                                    if (selectedDate.HasValue)
                                        attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                            attribute, selectedDate.Value.ToString("D"));
                                }

                                break;
                            case AttributeControlType.FileUpload:
                                {
                                    _ = Guid.TryParse(form[controlId], out var downloadGuid);
                                    var download = await _downloadService.GetDownloadByGuidAsync(downloadGuid);
                                    if (download != null)
                                    {
                                        attributesXml = _checkoutAttributeParser.AddCheckoutAttribute(attributesXml,
                                                   attribute, download.DownloadGuid.ToString());
                                    }
                                }

                                break;
                            default:
                                break;
                        }
                    }

                    //validate conditional attributes (if specified)
                    foreach (var attribute in checkoutAttributes)
                    {
                        var conditionMet = await _checkoutAttributeParser.IsConditionMetAsync(attribute, attributesXml);
                        if (conditionMet.HasValue && !conditionMet.Value)
                            attributesXml = _checkoutAttributeParser.RemoveCheckoutAttribute(attributesXml, attribute);
                    }

                    //save checkout attributes
                    await _genericAttributeService.SaveAttributeAsync(await _workContext.GetCurrentCustomerAsync(), NopCustomerDefaults.CheckoutAttributes, attributesXml, store.Id);
                    trx.Commit();
                }
                catch{
                    trx.Rollback();
                }
            }
        }

        #endregion

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("updatecart")]
        public virtual async Task<IActionResult> UpdateCart(IFormCollection form)
        {
           
            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            return View(model);
        }

    }
}