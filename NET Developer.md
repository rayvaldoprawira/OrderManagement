.NET Developer

Latar Belakang

Klien kami sebuah perusahaan distribusi sedang mengalami beberapa masalah di sistem order

management mereka:

1\. Sering terjadi double order karena user impatient mengklik tombol "Submit" berulang kali.

2\. Stock inventory sering minus karena dua order masuk bersamaan di milidetik yang sama

untuk produk yang sama, dan sistem tidak handle ini dengan benar.

3\. Status order kadang ter-update dengan tidak konsisten saat ada multiple admin yang

meng-update order yang sama secara bersamaan.

4\. Tim ops sulit melakukan tracing error karena minimnya logging.

5\. Tim Anda diminta membuat prototype service baru sebagai fondasi rewrite sistem ini.

Tugas

Buat REST API dengan ASP.NET Core untuk Order Management dengan requirement berikut:

Functional Requirements:

Create Order — User membuat order yang berisi: customerId, list of items (productId, quantity),

shippingAddress.

Get Order — Ambil detail order berdasarkan ID.

List Orders — List order dengan filter (status, customerId, date range) dan pagination.

Update Order Status — Update status order.

Hanya transisi tertentu yang valid:

Pending → Confirmed | Cancelled

Confirmed → Shipped | Cancelled

Shipped → Delivered

Delivered / Cancelled → tidak bisa diubah lagi (terminal state)

Cancel Order — Cancel order. Hanya bisa dilakukan jika status masih Pending atau Confirmed.

Saat cancel, stock harus dikembalikan.

Stock Management (penting untuk testing concurrency)

Karena ini prototype, tidak perlu service inventory terpisah. Cukup buat tabel Products sederhana

dengan kolom Id, Name, StockQuantity, Price.

Aturan:

Saat order dibuat, stock harus berkurang sesuai quantity.

Kalau stock tidak cukup, order ditolak (return error yang sesuai).

Saat order di-cancel, stock harus dikembalikan.

Stock tidak boleh pernah minus dalam kondisi apapun, termasuk concurrent request.

Non-Functional Requirements (Wajib)

1\. Idempotency

Cegah double order saat client retry atau double-click. Strategi bebas, bisa via

Idempotency-Key header, hash payload, atau pendekatan lain. Justifikasi pilihan di

README.

2\. Concurrency Handling (Fokus utama)

Sistem Anda harus tahan terhadap skenario concurrent berikut:

a. Skenario A (Concurrent Stock Deduction):

Dua user submit order yang sama-sama membutuhkan 10 unit Product X,

padahal stock tinggal 15. Sistem harus memastikan hanya satu yang berhasil, atau

total stock terdeduksi tidak boleh > 15.

b. Skenario B (Concurrent Status Update):

Dua admin meng-update status order yang sama secara bersamaan (misal

satu update jadi Shipped, satu lagi cancel jadi Cancelled). Sistem harus

memastikan hanya satu yang menang, dan yang lain mendapat error yang jelas.

c. Skenario C (Idempotent Create Under Race):

Dua request POST /orders dengan idempotency key yang sama tiba di

server bersamaan persis (sebelum salah satu sempat commit). Sistem harus tetap

hanya membuat satu order.

Anda bebas memilih strategi: optimistic locking, pessimistic locking, database

constraint, atomic SQL operation, atau kombinasi. Tapi Anda harus bisa menjelaskan

kenapa pilih itu saat presentasi.

3\. Race Condition Prevention

Selain concurrency di atas, identifikasi sendiri minimal 2 race condition lain yang

mungkin terjadi di kode Anda dan jelaskan bagaimana Anda mencegahnya. Tulis ini di

README.

4\. Validasi & Error Handling

Response error konsisten format yang sama untuk semua endpoint. Status code

tepat (400, 404, 409, 422, dll).

5\. Logging

Logging yang cukup untuk men-trace request di production. Bukan

Console.WriteLine. Setiap request idealnya punya correlation ID yang bisa ditrace antar

log.

6\. Persistensi Data

SQL Server / PostgreSQL / SQLite (sebutkan alasan pilihan). Sertakan migration

atau script schema.

7\. Testing

Sertakan test yang menurut Anda paling bernilai. Wajib ada minimal 1 test yang

menguji skenario concurrent (skenario A, B, atau C di atas).

Submission: Git repository (GitHub/GitLab/Bitbucket), kirim link ke WA HR yang menghubungi.