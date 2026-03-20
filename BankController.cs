using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MilliBankaAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BankaController : ControllerBase
    {
        private string _dbAdresi = $"Data Source={Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}\\MilliBankaDb.db";

        public BankaController()
        {
            using (var conn = new SqliteConnection(_dbAdresi))
            {
                conn.Open();
                string sql = @"
                    CREATE TABLE IF NOT EXISTS Musteriler (TC TEXT PRIMARY KEY, AdSoyad TEXT, Sifre TEXT, Bakiye DECIMAL);
                    CREATE TABLE IF NOT EXISTS Islemler (Id INTEGER PRIMARY KEY AUTOINCREMENT, TC TEXT, Tip TEXT, Miktar DECIMAL, Tarih DATETIME DEFAULT CURRENT_TIMESTAMP);";
                using (var cmd = new SqliteCommand(sql, conn)) { cmd.ExecuteNonQuery(); }
            }
        }

        [HttpPost("transfer-yap")]
        public IActionResult TransferYap(string gonderenTc, string gonderenSifre, string aliciTc, decimal miktar)
        {
            try
            {
                if (miktar <= 0) return BadRequest(new { mesaj = "Geçersiz Miktar" });
                using (var conn = new SqliteConnection(_dbAdresi))
                {
                    conn.Open();
                    using (var trans = conn.BeginTransaction())
                    {
                        string sqlGonderen = "UPDATE Musteriler SET Bakiye = Bakiye - @m WHERE TC = @gtc AND Sifre = @gsifre AND Bakiye >= @m";
                        using (var cmdG = new SqliteCommand(sqlGonderen, conn, trans))
                        {
                            cmdG.Parameters.AddWithValue("@m", miktar);
                            cmdG.Parameters.AddWithValue("@gtc", gonderenTc);
                            cmdG.Parameters.AddWithValue("@gsifre", gonderenSifre);

                            int etkilenen = cmdG.ExecuteNonQuery();
                            if (etkilenen == 0)
                            {
                                return BadRequest(new { mesaj = "Yetersiz bakiye veya hatalı bilgiler!" });
                            }
                        }

                        string sqlAlici = "UPDATE Musteriler SET Bakiye = Bakiye + @m WHERE TC = @atc";
                        using (var cmdA = new SqliteCommand(sqlAlici, conn, trans))
                        {
                            cmdA.Parameters.AddWithValue("@m", miktar);
                            cmdA.Parameters.AddWithValue("@atc", aliciTc);

                            if (cmdA.ExecuteNonQuery() == 0)
                            {
                                trans.Rollback();
                                return BadRequest(new { mesaj = "Alıcı bulunamadı!" });
                            }
                        }

                        string log = "INSERT INTO Islemler (TC, Tip, Miktar) VALUES (@tc, @tip, @m)";
                        using (var l1 = new SqliteCommand(log, conn, trans))
                        {
                            l1.Parameters.AddWithValue("@tc", gonderenTc);
                            l1.Parameters.AddWithValue("@tip", aliciTc + " Hesabına Transfer");
                            l1.Parameters.AddWithValue("@m", -miktar);
                            l1.ExecuteNonQuery();
                        }
                        using (var l2 = new SqliteCommand(log, conn, trans))
                        {
                            l2.Parameters.AddWithValue("@tc", aliciTc);
                            l2.Parameters.AddWithValue("@tip", gonderenTc + " Hesabından Gelen");
                            l2.Parameters.AddWithValue("@m", miktar);
                            l2.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return Ok(new { mesaj = "Transfer başarıyla tamamlandı." });
                    }
                }
            }
            catch (Exception ex) { return Problem(ex.Message); }
        }

        [HttpPost("para-yatir")]
        public IActionResult ParaYatir(string tc, string sifre, decimal miktar)
        {
            try
            {
                using (var conn = new SqliteConnection(_dbAdresi))
                {
                    conn.Open();
                    string sql = "UPDATE Musteriler SET Bakiye = Bakiye + @m WHERE TC = @tc AND Sifre = @s";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@m", miktar);
                        cmd.Parameters.AddWithValue("@tc", tc);
                        cmd.Parameters.AddWithValue("@s", sifre);
                        if (cmd.ExecuteNonQuery() == 0) return Unauthorized(new { mesaj = "Hatalı Giriş!" });
                    }
                    string log = "INSERT INTO Islemler (TC, Tip, Miktar) VALUES (@tc, 'Para Yatırma', @m)";
                    using (var cl = new SqliteCommand(log, conn)) { cl.Parameters.AddWithValue("@tc", tc); cl.Parameters.AddWithValue("@m", miktar); cl.ExecuteNonQuery(); }
                }
                return Ok(new { mesaj = miktar + " TL yatırıldı." });
            }
            catch (Exception ex) { return Problem(ex.Message); }
        }

        [HttpPost("para-cek")]
        public IActionResult ParaCek(string tc, string sifre, decimal miktar)
        {
            try
            {
                using (var conn = new SqliteConnection(_dbAdresi))
                {
                    conn.Open();
                    string sql = "UPDATE Musteriler SET Bakiye = Bakiye - @m WHERE TC = @tc AND Sifre = @s AND Bakiye >= @m";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@m", miktar);
                        cmd.Parameters.AddWithValue("@tc", tc);
                        cmd.Parameters.AddWithValue("@s", sifre);
                        if (cmd.ExecuteNonQuery() == 0) return BadRequest(new { mesaj = "Yetersiz bakiye!" });
                    }
                    string log = "INSERT INTO Islemler (TC, Tip, Miktar) VALUES (@tc, 'Para Çekme', @m)";
                    using (var cl = new SqliteCommand(log, conn)) { cl.Parameters.AddWithValue("@tc", tc); cl.Parameters.AddWithValue("@m", -miktar); cl.ExecuteNonQuery(); }
                }
                return Ok(new { mesaj = miktar + " TL çekildi." });
            }
            catch (Exception ex) { return Problem(ex.Message); }
        }

        [HttpGet("islem-gecmisi")]
        public IActionResult GetHistory(string tc, string sifre)
        {
            var list = new List<object>();
            using (var conn = new SqliteConnection(_dbAdresi))
            {
                conn.Open();
                string sql = "SELECT Tip, Miktar, Tarih FROM Islemler WHERE TC = @tc ORDER BY Tarih DESC";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tc", tc);
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read()) { list.Add(new { tip = rd.GetString(0), miktar = rd.GetDecimal(1), tarih = rd.GetDateTime(2).ToString("dd.MM HH:mm") }); }
                    }
                }
            }
            return Ok(list);
        }

        [HttpGet("bakiye-sorgula")]
        public IActionResult GetBakiye(string tc, string sifre)
        {
            using (var conn = new SqliteConnection(_dbAdresi))
            {
                conn.Open();
                string sql = "SELECT AdSoyad, Bakiye FROM Musteriler WHERE TC = @tc AND Sifre = @s";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tc", tc);
                    cmd.Parameters.AddWithValue("@s", sifre);
                    using (var rd = cmd.ExecuteReader()) { if (rd.Read()) return Ok(new { mesaj = rd.GetString(0), mevcutBakiye = rd.GetDecimal(1) }); }
                }
            }
            return Unauthorized(new { mesaj = "Hatalı!" });
        }
    }
}
