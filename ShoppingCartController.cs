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

        #region Shopping cart

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("updatecart")]
        public virtual async Task<IActionResult> UpdateCart(IFormCollection form)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableShoppingCart))
                return RedirectToRoute("Homepage");

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            //get identifiers of items to remove
            var itemIdsToRemove = form["removefromcart"]
                .SelectMany(value => value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(idString => int.TryParse(idString, out var id) ? id : 0)
                .Distinct().ToList();

            var products = (await _productService.GetProductsByIdsAsync(cart.Select(item => item.ProductId).Distinct().ToArray()))
                .ToDictionary(item => item.Id, item => item);

            //get order items with changed quantity
            var itemsWithNewQuantity = cart.Select(item => new
            {
                //try to get a new quantity for the item, set 0 for items to remove
                NewQuantity = itemIdsToRemove.Contains(item.Id) ? 0 : int.TryParse(form[$"itemquantity{item.Id}"], out var quantity) ? quantity : item.Quantity,
                Item = item,
                Product = products.ContainsKey(item.ProductId) ? products[item.ProductId] : null
            }).Where(item => item.NewQuantity != item.Item.Quantity);

            //order cart items
            //first should be items with a reduced quantity and that require other products; or items with an increased quantity and are required for other products
            var orderedCart = await itemsWithNewQuantity
                .OrderByDescendingAwait(async cartItem =>
                    (cartItem.NewQuantity < cartItem.Item.Quantity &&
                     (cartItem.Product?.RequireOtherProducts ?? false)) ||
                    (cartItem.NewQuantity > cartItem.Item.Quantity && cartItem.Product != null && (await _shoppingCartService
                         .GetProductsRequiringProductAsync(cart, cartItem.Product)).Any()))
                .ToListAsync();

            //try to update cart items with new quantities and get warnings
            var warnings = await orderedCart.SelectAwait(async cartItem => new
            {
                ItemId = cartItem.Item.Id,
                Warnings = await _shoppingCartService.UpdateShoppingCartItemAsync(customer,
                    cartItem.Item.Id, cartItem.Item.AttributesXml, cartItem.Item.CustomerEnteredPrice,
                    cartItem.Item.RentalStartDateUtc, cartItem.Item.RentalEndDateUtc, cartItem.NewQuantity, true)
            }).ToListAsync();

            //updated cart
            cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            //prepare model
            var model = new ShoppingCartModel();
            model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart);

            //update current warnings
            foreach (var warningItem in warnings.Where(warningItem => warningItem.Warnings.Any()))
            {
                //find shopping cart item model to display appropriate warnings
                var itemModel = model.Items.FirstOrDefault(item => item.Id == warningItem.ItemId);
                if (itemModel != null)
                    itemModel.Warnings = warningItem.Warnings.Concat(itemModel.Warnings).Distinct().ToList();
            }

            return View(model);
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("continueshopping")]
        public virtual async Task<IActionResult> ContinueShopping()
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var returnUrl = await _genericAttributeService.GetAttributeAsync<string>(await _workContext.GetCurrentCustomerAsync(), NopCustomerDefaults.LastContinueShoppingPageAttribute, store.Id);

            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToRoute("Homepage");
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("checkout")]
        public virtual async Task<IActionResult> StartCheckout(IFormCollection form)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            //validate attributes
            var checkoutAttributes = await _genericAttributeService.GetAttributeAsync<string>(customer,
                NopCustomerDefaults.CheckoutAttributes, store.Id);
            var checkoutAttributeWarnings = await _shoppingCartService.GetShoppingCartWarningsAsync(cart, checkoutAttributes, true);
            if (checkoutAttributeWarnings.Any())
            {
                //something wrong, redisplay the page with warnings
                var model = new ShoppingCartModel();
                model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart, validateCheckoutAttributes: true);
                return View(model);
            }

            var anonymousPermissed = _orderSettings.AnonymousCheckoutAllowed
                                     && _customerSettings.UserRegistrationType == UserRegistrationType.Disabled;

            if (anonymousPermissed || !await _customerService.IsGuestAsync(customer))
                return RedirectToRoute("Checkout");

            var cartProductIds = cart.Select(ci => ci.ProductId).ToArray();
            var downloadableProductsRequireRegistration =
                _customerSettings.RequireRegistrationForDownloadableProducts && await _productService.HasAnyDownloadableProductAsync(cartProductIds);

            if (!_orderSettings.AnonymousCheckoutAllowed || downloadableProductsRequireRegistration)
            {
                //verify user identity (it may be facebook login page, or google, or local)
                return Challenge();
            }

            return RedirectToRoute("LoginCheckoutAsGuest", new { returnUrl = Url.RouteUrl("ShoppingCart") });
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("applydiscountcouponcode")]
        public virtual async Task<IActionResult> ApplyDiscountCoupon(string discountcouponcode, IFormCollection form)
        {
            //trim
            if (discountcouponcode != null)
                discountcouponcode = discountcouponcode.Trim();

            //cart
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            var model = new ShoppingCartModel();
            if (!string.IsNullOrWhiteSpace(discountcouponcode))
            {
                //we find even hidden records here. this way we can display a user-friendly message if it's expired
                var discounts = (await _discountService.GetAllDiscountsAsync(couponCode: discountcouponcode, showHidden: true))
                    .Where(d => d.RequiresCouponCode)
                    .ToList();
                if (discounts.Any())
                {
                    var userErrors = new List<string>();
                    var anyValidDiscount = await discounts.AnyAwaitAsync(async discount =>
                    {
                        var validationResult = await _discountService.ValidateDiscountAsync(discount, customer, new[] { discountcouponcode });
                        userErrors.AddRange(validationResult.Errors);

                        return validationResult.IsValid;
                    });

                    if (anyValidDiscount)
                    {
                        //valid
                        await _customerService.ApplyDiscountCouponCodeAsync(customer, discountcouponcode);
                        model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.Applied"));
                        model.DiscountBox.IsApplied = true;
                    }
                    else
                    {
                        if (userErrors.Any())
                            //some user errors
                            model.DiscountBox.Messages = userErrors;
                        else
                            //general error text
                            model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.WrongDiscount"));
                    }
                }
                else
                    //discount cannot be found
                    model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.CannotBeFound"));
            }
            else
                //empty coupon code
                model.DiscountBox.Messages.Add(await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.Empty"));

            model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart);

            return View(model);
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired("applygiftcardcouponcode")]
        public virtual async Task<IActionResult> ApplyGiftCard(string giftcardcouponcode, IFormCollection form)
        {
            //trim
            if (giftcardcouponcode != null)
                giftcardcouponcode = giftcardcouponcode.Trim();

            //cart
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            var model = new ShoppingCartModel();
            if (!await _shoppingCartService.ShoppingCartIsRecurringAsync(cart))
            {
                if (!string.IsNullOrWhiteSpace(giftcardcouponcode))
                {
                    var giftCard = (await _giftCardService.GetAllGiftCardsAsync(giftCardCouponCode: giftcardcouponcode)).FirstOrDefault();
                    var isGiftCardValid = giftCard != null && await _giftCardService.IsGiftCardValidAsync(giftCard);
                    if (isGiftCardValid)
                    {
                        await _customerService.ApplyGiftCardCouponCodeAsync(customer, giftcardcouponcode);
                        model.GiftCardBox.Message = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.Applied");
                        model.GiftCardBox.IsApplied = true;
                    }
                    else
                    {
                        model.GiftCardBox.Message = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");
                        model.GiftCardBox.IsApplied = false;
                    }
                }
                else
                {
                    model.GiftCardBox.Message = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");
                    model.GiftCardBox.IsApplied = false;
                }
            }
            else
            {
                model.GiftCardBox.Message = await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.DontWorkWithAutoshipProducts");
                model.GiftCardBox.IsApplied = false;
            }

            model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart);
            return View(model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> GetEstimateShipping(EstimateShippingModel model, IFormCollection form)
        {
            if (model == null)
                model = new EstimateShippingModel();

            var errors = new List<string>();

            if (!_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(model.ZipPostalCode))
                errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.ZipPostalCode.Required"));

            if (_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(model.City))
                errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.City.Required"));

            if (model.CountryId == null || model.CountryId == 0)
                errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.Country.Required"));

            if (errors.Count > 0)
                return Json(new
                {
                    Success = false,
                    Errors = errors
                });

            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart, store.Id);
            //parse and save checkout attributes
            await ParseAndSaveCheckoutAttributesAsync(cart, form);

            var result = await _shoppingCartModelFactory.PrepareEstimateShippingResultModelAsync(cart, model, true);

            return Json(result);
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired(FormValueRequirement.StartsWith, "removediscount-")]
        public virtual async Task<IActionResult> RemoveDiscountCoupon(IFormCollection form)
        {
            var model = new ShoppingCartModel();

            //get discount identifier
            var discountId = 0;
            foreach (var formValue in form.Keys)
                if (formValue.StartsWith("removediscount-", StringComparison.InvariantCultureIgnoreCase))
                    discountId = Convert.ToInt32(formValue["removediscount-".Length..]);
            var discount = await _discountService.GetDiscountByIdAsync(discountId);
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (discount != null)
                await _customerService.RemoveDiscountCouponCodeAsync(customer, discount.CouponCode);

            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart);
            return View(model);
        }

        [HttpPost, ActionName("Cart")]
        [FormValueRequired(FormValueRequirement.StartsWith, "removegiftcard-")]
        public virtual async Task<IActionResult> RemoveGiftCardCode(IFormCollection form)
        {
            var model = new ShoppingCartModel();

            //get gift card identifier
            var giftCardId = 0;
            foreach (var formValue in form.Keys)
                if (formValue.StartsWith("removegiftcard-", StringComparison.InvariantCultureIgnoreCase))
                    giftCardId = Convert.ToInt32(formValue["removegiftcard-".Length..]);
            var gc = await _giftCardService.GetGiftCardByIdAsync(giftCardId);
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (gc != null)
                await _customerService.RemoveGiftCardCouponCodeAsync(customer, gc.GiftCardCouponCode);

            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(model, cart);
            return View(model);
        }

        #endregion

        #region Wishlist

        public virtual async Task<IActionResult> Wishlist(Guid? customerGuid)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableWishlist))
                return RedirectToRoute("Homepage");

            var customer = customerGuid.HasValue ?
                await _customerService.GetCustomerByGuidAsync(customerGuid.Value)
                : await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                return RedirectToRoute("Homepage");

            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id);

            var model = new WishlistModel();
            model = await _shoppingCartModelFactory.PrepareWishlistModelAsync(model, cart, !customerGuid.HasValue);
            return View(model);
        }

        [HttpPost, ActionName("Wishlist")]
        [FormValueRequired("updatecart")]
        public virtual async Task<IActionResult> UpdateWishlist(IFormCollection form)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableWishlist))
                return RedirectToRoute("Homepage");

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id);

            var allIdsToRemove = form.ContainsKey("removefromcart")
                ? form["removefromcart"].ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList()
                : new List<int>();

            //current warnings <cart item identifier, warnings>
            var innerWarnings = new Dictionary<int, IList<string>>();
            foreach (var sci in cart)
            {
                var remove = allIdsToRemove.Contains(sci.Id);
                if (remove)
                    await _shoppingCartService.DeleteShoppingCartItemAsync(sci);
                else
                {
                    foreach (var formKey in form.Keys)
                        if (formKey.Equals($"itemquantity{sci.Id}", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (int.TryParse(form[formKey], out var newQuantity))
                            {
                                var currSciWarnings = await _shoppingCartService.UpdateShoppingCartItemAsync(customer,
                                    sci.Id, sci.AttributesXml, sci.CustomerEnteredPrice,
                                    sci.RentalStartDateUtc, sci.RentalEndDateUtc,
                                    newQuantity, true);
                                innerWarnings.Add(sci.Id, currSciWarnings);
                            }

                            break;
                        }
                }
            }

            //updated wishlist
            cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id);
            var model = new WishlistModel();
            model = await _shoppingCartModelFactory.PrepareWishlistModelAsync(model, cart);
            //update current warnings
            foreach (var kvp in innerWarnings)
            {
                //kvp = <cart item identifier, warnings>
                var sciId = kvp.Key;
                var warnings = kvp.Value;
                //find model
                var sciModel = model.Items.FirstOrDefault(x => x.Id == sciId);
                if (sciModel != null)
                    foreach (var w in warnings)
                        if (!sciModel.Warnings.Contains(w))
                            sciModel.Warnings.Add(w);
            }

            return View(model);
        }

        [HttpPost, ActionName("Wishlist")]
        [FormValueRequired("addtocartbutton")]
        public virtual async Task<IActionResult> AddItemsToCartFromWishlist(Guid? customerGuid, IFormCollection form)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableShoppingCart))
                return RedirectToRoute("Homepage");

            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableWishlist))
                return RedirectToRoute("Homepage");

            var customer = await _workContext.GetCurrentCustomerAsync();
            var pageCustomer = customerGuid.HasValue
                ? await _customerService.GetCustomerByGuidAsync(customerGuid.Value)
                : customer;
            if (pageCustomer == null)
                return RedirectToRoute("Homepage");

            var store = await _storeContext.GetCurrentStoreAsync();
            var pageCart = await _shoppingCartService.GetShoppingCartAsync(pageCustomer, ShoppingCartType.Wishlist, store.Id);

            var allWarnings = new List<string>();
            var countOfAddedItems = 0;
            var allIdsToAdd = form.ContainsKey("addtocart")
                ? form["addtocart"].ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()
                : new List<int>();
            foreach (var sci in pageCart)
            {
                if (allIdsToAdd.Contains(sci.Id))
                {
                    var product = await _productService.GetProductByIdAsync(sci.ProductId);

                    var warnings = await _shoppingCartService.AddToCartAsync(customer,
                        product, ShoppingCartType.ShoppingCart,
                        store.Id,
                        sci.AttributesXml, sci.CustomerEnteredPrice,
                        sci.RentalStartDateUtc, sci.RentalEndDateUtc, sci.Quantity, true);
                    if (!warnings.Any())
                        countOfAddedItems++;
                    if (_shoppingCartSettings.MoveItemsFromWishlistToCart && //settings enabled
                        !customerGuid.HasValue && //own wishlist
                        !warnings.Any()) //no warnings ( already in the cart)
                    {
                        //let's remove the item from wishlist
                        await _shoppingCartService.DeleteShoppingCartItemAsync(sci);
                    }

                    allWarnings.AddRange(warnings);
                }
            }

            if (countOfAddedItems > 0)
            {
                //redirect to the shopping cart page

                if (allWarnings.Any())
                {
                    _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Wishlist.AddToCart.Error"));
                }

                return RedirectToRoute("ShoppingCart");
            }
            else
            {
                _notificationService.WarningNotification(await _localizationService.GetResourceAsync("Wishlist.AddToCart.NoAddedItems"));
            }
            //no items added. redisplay the wishlist page

            if (allWarnings.Any())
            {
                _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Wishlist.AddToCart.Error"));
            }

            var cart = await _shoppingCartService.GetShoppingCartAsync(pageCustomer, ShoppingCartType.Wishlist, store.Id);

            var model = new WishlistModel();
            model = await _shoppingCartModelFactory.PrepareWishlistModelAsync(model, cart, !customerGuid.HasValue);
            return View(model);
        }

        public virtual async Task<IActionResult> EmailWishlist()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableWishlist) || !_shoppingCartSettings.EmailWishlistEnabled)
                return RedirectToRoute("Homepage");

            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.Wishlist, store.Id);

            if (!cart.Any())
                return RedirectToRoute("Homepage");

            var model = new WishlistEmailAFriendModel();
            model = await _shoppingCartModelFactory.PrepareWishlistEmailAFriendModelAsync(model, false);
            return View(model);
        }

        [HttpPost, ActionName("EmailWishlist")]
        [FormValueRequired("send-email")]
        [ValidateCaptcha]
        public virtual async Task<IActionResult> EmailWishlistSend(WishlistEmailAFriendModel model, bool captchaValid)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.EnableWishlist) || !_shoppingCartSettings.EmailWishlistEnabled)
                return RedirectToRoute("Homepage");

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id);

            if (!cart.Any())
                return RedirectToRoute("Homepage");

            //validate CAPTCHA
            if (_captchaSettings.Enabled && _captchaSettings.ShowOnEmailWishlistToFriendPage && !captchaValid)
            {
                ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Common.WrongCaptchaMessage"));
            }

            //check whether the current customer is guest and ia allowed to email wishlist
            if (await _customerService.IsGuestAsync(customer) && !_shoppingCartSettings.AllowAnonymousUsersToEmailWishlist)
            {
                ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Wishlist.EmailAFriend.OnlyRegisteredUsers"));
            }

            if (ModelState.IsValid)
            {
                //email
                await _workflowMessageService.SendWishlistEmailAFriendMessageAsync(customer,
                        (await _workContext.GetWorkingLanguageAsync()).Id, model.YourEmailAddress,
                        model.FriendEmail, _htmlFormatter.FormatText(model.PersonalMessage, false, true, false, false, false, false));

                model.SuccessfullySent = true;
                model.Result = await _localizationService.GetResourceAsync("Wishlist.EmailAFriend.SuccessfullySent");

                return View(model);
            }

            //If we got this far, something failed, redisplay form
            model = await _shoppingCartModelFactory.PrepareWishlistEmailAFriendModelAsync(model, true);

            return View(model);
        }

        #endregion
    }
}