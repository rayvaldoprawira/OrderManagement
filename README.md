# Order Management System (Prototype REST API)

RESTful API untuk sistem Order Management dengan fokus utama pada **High Concurrency Handling**, **Idempotency**, **Race Condition Prevention**, **Audit Logging/Tracing**, dan **Clean Architecture** menggunakan **ASP.NET Core (.NET 10)** dan **Entity Framework Core dengan Microsoft SQL Server**.

---

## Daftar Isi
1. [Arsitektur Proyek](#arsitektur-proyek)
2. [Fitur Fungsional & Aturan Bisnis](#fitur-fungsional--aturan-bisnis)
3. [Strategi Non-Fungsional (Concurrency & Idempotency)](#strategi-non-fungsional)
   - [1. Idempotency Handling & Justifikasi](#1-idempotency-handling--justifikasi)
   - [2. Concurrency Handling (Skenario A, B, C)](#2-concurrency-handling)
   - [3. Identifikasi & Pencegahan Race Condition Tambahan](#3-identifikasi--pencegahan-race-condition-tambahan)
   - [4. Validasi & Format Error Konsisten](#4-validasi--format-error-konsisten)
   - [5. Structured Logging & Correlation ID Tracing](#5-structured-logging--correlation-id-tracing)
4. [Persistensi Data & Skema Database](#persistensi-data--skema-database)
5. [Prasyarat & Cara Menjalankan Aplikasi](#prasyarat--cara-menjalankan-aplikasi)
6. [Dokumentasi API & Contoh Request](#dokumentasi-api--contoh-request)
7. [Automated Testing & Concurrency Test Verification](#automated-testing)

---

## Arsitektur Proyek

Aplikasi ini dibangun menggunakan arsitektur modular **Clean Architecture**:

```
OrderManagement/
│
├── src/
│   ├── OrderManagement.Domain/             # Core Domain (Entities, Enums, Domain Exceptions)
│   │   ├── Entities/                       # Product, Order, OrderItem, IdempotencyRecord
│   │   ├── Enums/                          # OrderStatus
│   │   └── Exceptions/                     # DomainException, InsufficientStockException, ConcurrencyConflictException, dll.
│   │
│   ├── OrderManagement.Application/        # Application Logic & Contracts
│   │   ├── DTOs/                           # CreateOrderRequest, OrderResponse, PagedResult, ApiResponse<T>
│   │   ├── Interfaces/                     # IOrderService, IProductService, IIdempotencyService
│   │   └── Validators/                     # FluentValidation validators
│   │
│   ├── OrderManagement.Infrastructure/     # Persistence & Database Implementation
│   │   ├── Data/                           # OrderDbContext, DbInitializer, EF Migrations
│   │   └── Services/                       # OrderService, ProductService, IdempotencyService
│   │
│   └── OrderManagement.Api/                # Web API Presentation Layer
│       ├── Controllers/                    # OrdersController, ProductsController
│       ├── Middleware/                     # CorrelationIdMiddleware, ExceptionHandlingMiddleware
│       ├── Program.cs                      # Dependency Injection, Serilog, Swagger & Pipeline Config
│       └── appsettings.json                # Database Connection String & Logging Setup
│
└── tests/
    └── OrderManagement.Tests/              # Unit & Concurrency Test Suite (xUnit + FluentAssertions)
        ├── ConcurrencyTests.cs             # Automated tests for Scenario A, B, and C
        ├── OrderStateTransitionTests.cs    # State-machine transition tests
        └── OrderServiceTests.cs            # Business rules & CRUD tests
```

---

##  Fitur Fungsional & Aturan Bisnis

1. **Create Order (`POST /api/orders`)**:
   - Menerima `customerId`, `shippingAddress`, dan daftar items (`productId`, `quantity`).
   - Mengurangi stok barang secara otomatis dan aman dari *race condition*.
   - Jika stok tidak mencukupi, request ditolak dengan kode `422 Unprocessable Entity` disertai detail barang yang kurang.
   - Mendukung header `Idempotency-Key` untuk mencegah duplikasi order.
2. **Get Order Details (`GET /api/orders/{id}`)**:
   - Mengambil detail order lengkap dengan item, harga subtotal, total harga, status, dan concurrency token.
3. **List Orders (`GET /api/orders`)**:
   - Mendukung filter: `customerId`, `status`, rentang tanggal (`fromDate`, `toDate`).
   - Dilengkapi paginasi (`pageNumber`, `pageSize`, `totalCount`, `totalPages`).
4. **Update Order Status (`PATCH /api/orders/{id}/status`)**:
   - Menjalankan *state machine* transisi status yang valid:
     - `Pending` ➔ `Confirmed` atau `Cancelled`
     - `Confirmed` ➔ `Shipped` atau `Cancelled`
     - `Shipped` ➔ `Delivered`
     - `Delivered` & `Cancelled` ➔ **Terminal State** (tidak dapat diubah lagi).
5. **Cancel Order (`POST /api/orders/{id}/cancel`)**:
   - Hanya dapat dibatalkan jika status masih `Pending` atau `Confirmed`.
   - Mengembalikan (*restore*) stok produk ke database secara otomatis.

---

##  Strategi Non-Fungsional

### 1. Idempotency Handling & Justifikasi
- **Masalah:** User tidak sabar mengklik tombol "Submit" berulang kali, atau retry otomatis dari peramban/jaringan menyebabkan *double order*.
- **Solusi yang Dipilih:** 
  - Menggunakan header HTTP `Idempotency-Key` (standar industri IETF).
  - Client menyertakan UUID unik di header setiap kali melakukan checkout.
  - Backend menghitung SHA-256 hash dari payload request (`RequestHash`) untuk memastikan payload tidak berubah dengan key yang sama.
  - Backend mencatat record ke tabel `IdempotencyRecords` dengan status `Processing`.
  - Kolom `Key` pada tabel memiliki **Primary Key / Unique Index** di level SQL Server.
- **Justifikasi:**
  - Database Unique Constraint adalah satu-satunya jaminan deterministik di sistem terdistribusi untuk mencegah *race condition* saat dua request tiba bersamaan pada milidetik yang sama (Skenario C).
  - Jika request kedua tiba saat request pertama masih `Processing`, sistem mendeteksi *race* dan menunggu/mengembalikan status `409 Conflict`.
  - Jika request kedua tiba setelah request pertama `Completed`, sistem langsung mengembalikan cached response `OrderResponse` yang identik tanpa memotong stok kembali.

---

### 2. Concurrency Handling

#### a. Skenario A (Concurrent Stock Deduction)
- **Kondisi:** Dua user submit order yang sama-sama membutuhkan 10 unit Product X, padahal stok tinggal 15.
- **Pencegahan:**
  1. **Atomic Conditional SQL Decrement with Row-Level Locking:**
     ```sql
     UPDATE Products 
     SET StockQuantity = StockQuantity - @quantity 
     WHERE Id = @productId AND StockQuantity >= @quantity;
     ```
     Dengan klausa `AND StockQuantity >= @quantity`, SQL Server mengakuisisi exclusive update lock (`UPDLOCK/ROWLOCK`). Transaksi pertama berhasil mengurangi stok menjadi 5 (affected rows = 1). Transaksi kedua yang tiba bersamaan akan mendapati kondisi `StockQuantity >= 10` bernilai `false`, menghasilkan affected rows = 0.
  2. Transaksi kedua segera mendeteksi kegagalan, membatalkan transaksi (*rollback*), dan mengembalikan `InsufficientStockException` (HTTP `422 Unprocessable Entity`).
  3. **Engine-Level Defense:** Ditambahkan Check Constraint di SQL Server:
     ```sql
     CONSTRAINT [CK_Products_StockQuantity_NonNegative] CHECK ([StockQuantity] >= 0)
     ```
     Ini menjamin di level *storage engine* bahwa stok **tidak akan pernah bisa minus** dalam kondisi apapun.

#### b. Skenario B (Concurrent Status Update)
- **Kondisi:** Dua admin meng-update order yang sama secara bersamaan (misal Admin 1 mengubah jadi `Shipped`, Admin 2 mengubah jadi `Cancelled`).
- **Pencegahan:**
  1. **Optimistic Concurrency Control (OCC) menggunakan `RowVersion`:**
     Setiap entitas `Order` memiliki kolom concurrency token bertipe SQL Server `rowversion` (`byte[]`).
     ```csharp
     entity.Property(o => o.RowVersion).IsRowVersion();
     ```
  2. Saat update dieksekusi, EF Core menyertakan `WHERE RowVersion = @originalRowVersion`.
  3. Admin yang commit lebih dulu berhasil memperbarui data dan mengubah nilai `RowVersion` di database.
  4. Admin kedua yang mencoba mengupdate dengan data usang (*stale*) akan otomatis memicu `DbUpdateConcurrencyException`.
  5. Exception ditangkap dan dikonversi menjadi HTTP `409 Conflict` dengan pesan yang jelas:
     `"Order status was concurrently modified by another user. Current status is 'Shipped'. Please refresh and try again."`

#### c. Skenario C (Idempotent Create Under Race)
- **Kondisi:** Dua request `POST /orders` dengan `Idempotency-Key` yang sama persis tiba di server bersamaan di milidetik yang sama sebelum salah satunya sempat commit.
- **Pencegahan:**
  1. Tabel `IdempotencyRecords` menerapkan `PK_IdempotencyRecords` (Unique Index).
  2. Saat kedua thread mencoba melakukan `INSERT INTO IdempotencyRecords`, database engine SQL Server hanya memenangkan satu thread.
  3. Thread yang kalah akan mengalami `DbUpdateException` (pelanggaran kunci unik).
  4. Thread yang kalah menangkap exception ini, melakukan *backoff* singkat, dan mengambil hasil eksekusi dari thread pemenang, atau mengembalikan HTTP `409 Conflict`.
  5. Dengan demikian, **hanya satu order yang dibuat** dan stok hanya terpotong 1 kali.

---

### 3. Identifikasi & Pencegahan Race Condition Tambahan

Selain Skenario A, B, dan C, kami mengidentifikasi dan menangani 3 potensi *race condition* kritis lainnya:

#### 1. Deadlock pada Order dengan Multiple Products (Lock Ordering Race)
- **Masalah:** User A membuat order dengan item [Produk 1, Produk 2]. Di saat bersamaan, User B membuat order dengan item [Produk 2, Produk 1]. User A mengunci Produk 1 dan menunggu Produk 2, sementara User B mengunci Produk 2 dan menunggu Produk 1. Hal ini mengakibatkan **Database Deadlock** (`SqlException: Rerun the transaction. Process was deadlocked on lock resources`).
- **Pencegahan:**
  Sebelum mengunci atau mengeksekusi query stok, sistem selalu melakukan pengurutan deterministik (*deterministic lock hierarchy*):
  ```csharp
  var consolidatedItems = request.Items
      .GroupBy(i => i.ProductId)
      .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
      .OrderBy(i => i.ProductId) // Kunci hierarki terurut untuk mencegah deadlock
      .ToList();
  ```
  Dengan selalu mengakses dan mengunci resource dengan urutan `ProductId` menaik, siklus *deadlock* tidak akan pernah terjadi.

#### 2. Order Cancellation Race vs Shipping Update
- **Masalah:** Admin sedang menandai order sebagai `Shipped` bersamaan dengan customer mengklik tombol `Cancel`. Jika tidak dilindungi, order bisa tercatat `Shipped` namun stoknya dikembalikan ke gudang (terjadi *double stock*).
- **Pencegahan:**
  Operasi pembatalan order (`CancelOrderAsync`) dan perubahan status dibungkus dalam transaksi database dengan pengecekan `RowVersion` dan validasi state machine. Jika status sudah berubah menjadi `Shipped`, metode `Cancel()` pada Domain Entity melempar `InvalidOrderStateTransitionException`, membatalkan pengembalian stok.

#### 3. Duplicate Items Aggregation dalam Satu Payload
- **Masalah:** Client mengirimkan list item yang memuat `ProductId` yang sama dua kali (misal item 1: Produk A qty 2; item 2: Produk A qty 3). Jika dieksekusi terpisah, dapat terjadi double validation atau race internal pada baris data yang sama.
- **Pencegahan:**
  Sistem melakukan konsolidasi dan agregasi quantity di awal sebelum interaksi database via `.GroupBy(i => i.ProductId).Select(...)`.

---

### 4. Validasi & Format Error Konsisten

Seluruh response error diformat seragam menggunakan class `ApiResponse<T>`:

```json
{
  "success": false,
  "message": "Insufficient stock for product 'Product X' (ID: 11111111-1111-1111-1111-111111111111). Available: 5, Requested: 10.",
  "data": null,
  "errors": null,
  "correlationId": "4a719d3637cb4eb7981541cb915f0eb1",
  "timestamp": "2026-10-20T20:25:00.0000000Z"
}
```

Mapping Status Code HTTP yang Tepat:
- `200 OK`: Pengambilan data / update berhasil.
- `201 Created`: Pembuatan order baru berhasil (disertai header `Location`).
- `400 Bad Request`: Validasi input gagal (FluentValidation) atau transisi status tidak valid.
- `404 Not Found`: Order atau Product tidak ditemukan.
- `409 Conflict`: Terjadi konflik konkurensi (optimistic lock mismatch / idempotency in progress).
- `422 Unprocessable Entity`: Stok gudang tidak mencukupi untuk memenuhi order.
- `500 Internal Server Error`: Kesalahan server yang tidak terduga.

---

### 5. Structured Logging & Correlation ID Tracing

- **Library:** Serilog terintegrasi dengan ASP.NET Core Hosting.
- **Correlation ID Middleware:** 
  - Membaca header `X-Correlation-ID` dari request yang masuk, atau mengenerate UUID baru jika tidak ada.
  - Menyertakan kembali `X-Correlation-ID` pada response headers.
  - Mendorong `CorrelationId` ke dalam `Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId)`.
  - Format output console:
    `[20:25:22 INF] [4a719d3637cb4eb7981541cb915f0eb1] Order a3f4... created for customer cust-01`
- Memudahkan tim operasional melakukan *tracing end-to-end* untuk setiap request di lingkungan production.

---

##  Persistensi Data & Skema Database

- **Database Engine:** Microsoft SQL Server (didukung melalui LocalDB bawaan Windows atau SQL Server Express/Standard).
- **Alasan Pemilihan:**
  - Jaminan penuh transaksi ACID (*Atomicity, Consistency, Isolation, Durability*).
  - Dukungan native tipe data `rowversion` untuk Optimistic Concurrency Control tanpa dependensi kolom timestamp manual.
  - Dukungan kuat untuk CHECK constraint dan Row-Level Locking (`UPDLOCK`, `ROWLOCK`).
  - Fitur `READ_COMMITTED_SNAPSHOT` yang otomatis aktif untuk performa tinggi tanpa *dirty reads*.
- **Skrip Skema SQL:** Skrip skema database lengkap tersedia pada file [`schema.sql`](schema.sql).

---

##  Prasyarat & Cara Menjalankan Aplikasi

### Prasyarat
1. [.NET 10 SDK](https://dotnet.microsoft.com/) atau versi .NET terbaru.
2. SQL Server (atau SQL Server LocalDB bawaan Visual Studio / Windows).

### Langkah Menjalankan

1. **Buka Terminal pada direktori proyek:**
   ```bash
   cd src/OrderManagement.Api
   ```

2. **Konfigurasi Connection String (Opsional jika ingin mengubah target database):**
   Pada file `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=OrderManagementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
   }
   ```
   *(Untuk SQL Server standalone dengan user & password dummy:*
   `"Server=localhost;Database=OrderManagementDb;User Id=DummyUser;Password=DummyPassword;TrustServerCertificate=True;"`*)*

3. **Jalankan Aplikasi:**
   ```bash
   dotnet run
   ```
   *Catatan: Saat aplikasi pertama kali berjalan, sistem secara otomatis mengeksekusi EF Core Migration dan seeding 4 produk awal (termasuk Product X untuk pengujian concurrency).*

4. **Buka Swagger UI:**
   Akses browser ke:
   `https://localhost:5001/` atau `http://localhost:5000/`

---

## 📖 Dokumentasi API & Contoh Request

### 1. Buat Order Baru (Create Order)
- **Endpoint:** `POST /api/orders`
- **Headers:**
  - `Content-Type: application/json`
  - `Idempotency-Key: 9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d` (Opsional tapi direkomendasikan)
- **Body:**
  ```json
  {
    "customerId": "CUST-001",
    "shippingAddress": "Jl. Sudirman No. 45, Jakarta",
    "items": [
      {
        "productId": "11111111-1111-1111-1111-111111111111",
        "quantity": 2
      }
    ]
  }
  ```

### 2. Update Status Order
- **Endpoint:** `PATCH /api/orders/{id}/status`
- **Body:**
  ```json
  {
    "status": 2
  }
  ```
  *(Status: 1 = Pending, 2 = Confirmed, 3 = Shipped, 4 = Delivered, 5 = Cancelled)*

### 3. Cancel Order (Restores Stock)
- **Endpoint:** `POST /api/orders/{id}/cancel`

### 4. List Orders dengan Filter & Pagination
- **Endpoint:** `GET /api/orders?customerId=CUST-001&status=1&pageNumber=1&pageSize=10`

---

##  Automated Testing

Proyek ini dilengkapi dengan **22 unit & concurrency tests** yang mencakup seluruh skenario wajib:

Jalankan test suite dengan perintah:
```bash
dotnet test
```

### Hasil Test Suite:
```
Test run for ...\OrderManagement.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 2 s
```

### Rincian Skenario Test:
1. `ScenarioA_ConcurrentStockDeduction_ShouldNeverResultInNegativeStock`:
   - 10 thread user submit order bersamaan masing-masing meminta 10 unit, pada produk dengan stok awal 15.
   - **Hasil:** Tepat 1 user berhasil (memotong 10 unit), 9 user ditolak dengan `InsufficientStockException`, dan sisa stok adalah 5 (tidak pernah minus).
2. `ScenarioB_ConcurrentStatusUpdate_ShouldAllowOnlyOneWinnerAndThrowConcurrencyConflict`:
   - 2 thread admin mencoba memperbarui status order yang sama secara bersamaan.
   - **Hasil:** Tepat 1 admin menang, admin kedua mendapati `ConcurrencyConflictException` (HTTP 409).
3. `ScenarioC_ConcurrentIdempotentCreate_ShouldCreateOnlyOneOrderAndDeductStockOnce`:
   - 5 request dengan `Idempotency-Key` yang sama dikirimkan di waktu bersamaan persis.
   - **Hasil:** Hanya 1 order yang terbuat di database, stok hanya terpotong 1 kali, dan seluruh request menerima Order ID yang sama.
4. `OrderStateTransitionTests`:
   - Menguji seluruh kombinasi transisi status valid dan penolakan perubahan pada *terminal state* (`Delivered` / `Cancelled`).
5. `CancelOrder_ShouldRestoreInventoryStock`:
   - Memverifikasi stok bertambah kembali setelah order dibatalkan.
