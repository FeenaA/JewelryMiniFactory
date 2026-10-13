using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace JewelryMiniFactory.Purchasing
{
    public sealed class IapPurchaseService : IDisposable
    {
        public const string ProductId = "coins_pack_small";
        private readonly Func<string, bool> fulfill;
        private readonly Dictionary<string, PendingOrder> pending = new Dictionary<string, PendingOrder>();
        private readonly HashSet<string> confirming = new HashSet<string>();
        private StoreController store;
        private Product product;
        private bool connected;
        private bool purchasesFetched;
        private bool purchaseRequested;
        private bool initialized;
        private bool disposed;

        public bool IsBusy => purchaseRequested || pending.Count != 0;
        public bool IsReady => connected && purchasesFetched && product != null && product.availableToPurchase;
        public bool CanPurchase => IsReady && !IsBusy && !disposed;
        public bool BlocksGameplayInput { get; private set; }
        public string Status { get; private set; } = "STORE LOADING";
        public event Action StateChanged;
        public event Action<string, bool> PurchaseFailed;

        public IapPurchaseService(Func<string, bool> fulfill)
        {
            this.fulfill = fulfill ?? throw new ArgumentNullException(nameof(fulfill));
        }

        public async Task InitializeAsync()
        {
            BlocksGameplayInput = true;
            Notify("STORE LOADING");
            try
            {
                store = UnityIAPServices.StoreController();
                store.ProcessPendingOrdersOnPurchasesFetched(false);
                store.OnStoreConnected += OnConnected;
                store.OnStoreDisconnected += OnDisconnected;
                store.OnProductsFetched += OnProductsFetched;
                store.OnProductsFetchFailed += OnProductsFailed;
                store.OnPurchasesFetched += OnPurchasesFetched;
                store.OnPurchasesFetchFailed += OnPurchasesFailed;
                store.OnPurchasePending += OnPending;
                store.OnPurchaseConfirmed += OnConfirmed;
                store.OnPurchaseFailed += OnFailed;
                store.OnPurchaseDeferred += OnDeferred;
                await store.Connect();
                if (disposed) return;
                initialized = true;
                connected = true;
                FetchProduct();
            }
            catch (Exception exception)
            {
                if (!disposed) Unavailable("Initialization failed: " + exception.Message);
            }
        }

        private void FetchProduct()
        {
            BlocksGameplayInput = true;
            Notify("STORE LOADING");
            purchasesFetched = false;
            store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(ProductId, ProductType.Consumable) }, new NoRetriesPolicy());
        }

        public void Buy()
        {
            if (!CanPurchase) { Debug.LogWarning("IAP: store unavailable or a purchase is already processing."); return; }
            purchaseRequested = true;
            BlocksGameplayInput = true;
            Notify("PURCHASE PROCESSING");
            try { store.PurchaseProduct(product); }
            catch (Exception exception)
            {
                purchaseRequested = false;
                BlocksGameplayInput = false;
                Notify("PURCHASE FAILED: " + exception.Message);
                ReportPurchaseFailure(exception.Message, false);
            }
        }

        private void OnConnected()
        {
            if (disposed) return;
            connected = true;
            if (initialized)
            {
                try { FetchProduct(); }
                catch (Exception exception) { Unavailable(exception.Message); }
            }
        }

        private void OnProductsFetched(List<Product> products)
        {
            if (disposed) return;
            product = products.FirstOrDefault(item => item.definition.id == ProductId && item.definition.type == ProductType.Consumable);
            if (product == null || !product.availableToPurchase) { Unavailable("Product not available: " + ProductId); return; }
            Debug.Log($"IAP: {product.metadata.localizedTitle}, {product.metadata.localizedPriceString}");
            try { store.FetchPurchases(); }
            catch (Exception exception) { Unavailable(exception.Message); }
        }

        private void OnPurchasesFetched(Orders orders)
        {
            if (disposed) return;
            purchasesFetched = true;
            BlocksGameplayInput = false;
            foreach (var order in orders.PendingOrders) OnPending(order);
            if (orders.DeferredOrders.Count != 0) purchaseRequested = true;
            Notify(IsBusy ? "PURCHASE PROCESSING" : "STORE READY");
        }

        private void OnPending(PendingOrder order)
        {
            if (disposed) return;
            // The payment dialog is finishing; pending persistence/confirmation must not freeze gameplay.
            BlocksGameplayInput = false;
            Notify("PURCHASE PROCESSING");
            try
            {
                var items = order?.CartOrdered?.Items()?.ToList();
                if (items == null || items.Count != 1 || items[0].Product.definition.id != ProductId ||
                    items[0].Product.definition.type != ProductType.Consumable || items[0].Quantity != 1 ||
                    string.IsNullOrWhiteSpace(order.Info?.TransactionID))
                {
                    purchaseRequested = true;
                    Notify("PURCHASE REJECTED: invalid product, quantity or transaction ID");
                    return;
                }
                var id = order.Info.TransactionID;
                pending[id] = order;
                purchaseRequested = false;
                if (confirming.Contains(id)) return;
                if (!fulfill(id))
                {
                    Notify("PURCHASE PENDING: reward could not be saved; not confirming");
                    return;
                }
                confirming.Add(id);
                Notify("PURCHASE CONFIRMING");
                store.ConfirmPurchase(order);
            }
            catch (Exception exception)
            {
                if (order?.Info?.TransactionID != null) confirming.Remove(order.Info.TransactionID);
                Notify("PURCHASE PENDING: " + exception.Message);
            }
        }

        private void OnConfirmed(Order order)
        {
            if (disposed) return;
            BlocksGameplayInput = false;
            var id = order?.Info?.TransactionID;
            if (id != null) confirming.Remove(id);
            if (order is ConfirmedOrder || (order is FailedOrder duplicate && duplicate.FailureReason == PurchaseFailureReason.DuplicateTransaction))
            {
                if (id != null) pending.Remove(id);
                purchaseRequested = false;
                Notify("PURCHASE SUCCEEDED");
            }
            else if (order is FailedOrder failed)
                Notify($"CONFIRMATION FAILED: {failed.FailureReason}, {failed.Details}; reward will not be granted again");
        }

        private void OnFailed(FailedOrder order)
        {
            BlocksGameplayInput = false;
            purchaseRequested = false;
            Notify(order.FailureReason == PurchaseFailureReason.UserCancelled ? "PURCHASE CANCELLED" :
                $"PURCHASE FAILED: {order.FailureReason}, {order.Details}");
            ReportPurchaseFailure(order.FailureReason.ToString(), order.FailureReason == PurchaseFailureReason.UserCancelled);
        }

        private void ReportPurchaseFailure(string reason, bool cancelled)
        {
            if (disposed || PurchaseFailed == null) return;
            foreach (Action<string, bool> handler in PurchaseFailed.GetInvocationList())
                try { handler(reason, cancelled); }
                catch (Exception) { /* Optional observers cannot affect purchase processing. */ }
        }

        private void OnDeferred(DeferredOrder order)
        {
            BlocksGameplayInput = false;
            purchaseRequested = true;
            Notify("PURCHASE DEFERRED: awaiting approval");
        }

        private void OnDisconnected(StoreConnectionFailureDescription failure)
        {
            confirming.Clear();
            Unavailable("Store disconnected: " + failure.Message);
        }
        private void OnProductsFailed(ProductFetchFailed failure) => Unavailable("Products failed: " + failure.FailureReason);
        private void OnPurchasesFailed(PurchasesFetchFailureDescription failure) => Unavailable("Purchases failed: " + failure.Message);

        private void Unavailable(string reason)
        {
            BlocksGameplayInput = false;
            connected = false;
            purchasesFetched = false;
            Notify("STORE UNAVAILABLE: " + reason);
        }

        private void Notify(string status)
        {
            if (disposed) return;
            Status = status;
            Debug.Log("IAP: " + status);
            StateChanged?.Invoke();
        }

        public void Dispose()
        {
            BlocksGameplayInput = false;
            disposed = true;
            if (store == null) return;
            store.OnStoreConnected -= OnConnected;
            store.OnStoreDisconnected -= OnDisconnected;
            store.OnProductsFetched -= OnProductsFetched;
            store.OnProductsFetchFailed -= OnProductsFailed;
            store.OnPurchasesFetched -= OnPurchasesFetched;
            store.OnPurchasesFetchFailed -= OnPurchasesFailed;
            store.OnPurchasePending -= OnPending;
            store.OnPurchaseConfirmed -= OnConfirmed;
            store.OnPurchaseFailed -= OnFailed;
            store.OnPurchaseDeferred -= OnDeferred;
            StateChanged = null;
            PurchaseFailed = null;
        }
    }
}
