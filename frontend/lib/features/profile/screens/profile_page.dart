import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/auth_provider.dart';
import '../../../providers/profile_provider.dart';
import '../../../providers/leaderboard_provider.dart';

import '../../../core/storage/client_id_storage.dart';
import '../../auth/screens/login_page.dart';
import '../../auth/screens/register_page.dart';
import '../../leaderboard/models/leaderboard_entry.dart';
import '../../auth/screens/email_verification_page.dart';
import 'password_management_page.dart';
import 'profile_achievements_page.dart';
import 'profile_history_page.dart';

class ProfilePage extends StatefulWidget {
  final Future<void> Function()? onAuthChanged;

  const ProfilePage({super.key, this.onAuthChanged});

  @override
  State<ProfilePage> createState() => ProfilePageState();
}

class ProfilePageState extends State<ProfilePage> {
  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  bool _isLoading = true;
  bool _isLoggedIn = false;
  bool _isLoggingOut = false;
  bool _hasPassword = false;

  String? _errorMessage;

  String _clientId = '';
  String? _username;
  String? _email;
  String? _firstName;
  String? _lastName;
  int? _userId;

  int _profileGeneration = 0;

  LeaderboardEntry? get _profileData {
    final auth = context.read<AuthProvider>();
    if (!auth.isLoggedIn || auth.userId != _userId) return null;
    return context
        .read<LeaderboardProvider>()
        .leaderboardFor(null, period: 'alltime')
        ?.allTime
        .currentUser;
  }

  @override
  void initState() {
    super.initState();
    _loadProfile();
  }

  Future<void> refreshProfile({bool forceRefresh = false}) async {
    if (!mounted || _isLoggingOut) return;
    await _loadProfile(forceRefresh: forceRefresh);
  }

  Future<void> _loadProfile({bool forceRefresh = false}) async {
    if (!mounted) return;
    final generation = ++_profileGeneration;
    final auth = context.read<AuthProvider>();
    final leaderboard = context.read<LeaderboardProvider>();
    try {
      await auth.initialize();
      final clientId = await _clientIdStorage.getClientId();
      if (!mounted || generation != _profileGeneration) return;
      setState(() {
        _clientId = clientId;
        _isLoggedIn = auth.isLoggedIn;
        _username = auth.username;
        _email = auth.email;
        _firstName = auth.firstName;
        _lastName = auth.lastName;
        _userId = auth.userId;
        _hasPassword = auth.hasPassword;
        _errorMessage = null;
        _isLoading = false;
      });
      if (auth.isLoggedIn) {
        context.read<ProfileProvider>().clear();
        await Future.wait([
          leaderboard.loadLeaderboard(
            period: 'alltime',
            forceRefresh: forceRefresh,
          ),
          context.read<ProfileProvider>().refresh(),
        ]);
      } else {
        context.read<ProfileProvider>().clear();
      }
    } catch (error) {
      if (!mounted || generation != _profileGeneration) return;
      if (kDebugMode) debugPrint('Profil yükleme hatası: $error');
      setState(() {
        _errorMessage = 'Profil bilgileri alınırken bir hata oluştu.';
        _isLoading = false;
      });
    }
  }

  Future<void> _linkGoogleAccount() async {
    final passwordController = TextEditingController();
    final password = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Google hesabını bağla'),
        content: TextField(
          controller: passwordController,
          obscureText: true,
          decoration: const InputDecoration(labelText: 'Mevcut şifren'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('İptal'),
          ),
          FilledButton(
            onPressed: () =>
                Navigator.pop(dialogContext, passwordController.text),
            child: const Text('Devam et'),
          ),
        ],
      ),
    );
    passwordController.dispose();
    if (password == null || password.isEmpty || !mounted) return;

    try {
      final result = await context.read<AuthProvider>().linkGoogleAccount(
        currentPassword: password,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            result.success
                ? 'Google hesabı bağlandı.'
                : result.message ?? 'Google hesabı bağlanamadı.',
          ),
        ),
      );
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Google hesabı bağlanamadı.')),
      );
    }
  }

  // Ad ve soyadı birleştirir.
  String get _displayFullName {
    final firstName = _firstName?.trim() ?? '';
    final lastName = _lastName?.trim() ?? '';

    final fullName = '$firstName $lastName'.trim();

    if (fullName.isNotEmpty) {
      return fullName;
    }

    // Eski kayıtlarda ad-soyad yoksa kullanıcı adını göster.
    final username = _username?.trim() ?? '';

    if (username.isNotEmpty) {
      return username;
    }

    return 'Satranç Oyuncusu';
  }

  String get _displayClientId {
    if (_clientId.isEmpty) return 'Oyuncu';

    if (_clientId.length <= 12) return _clientId;

    return '${_clientId.substring(0, 8)}...';
  }

  // Giriş ekranını açar.
  Future<void> _openLoginPage() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(builder: (_) => const LoginPage()),
    );

    if (!mounted) return;

    if (result == true) {
      // Önce hesap değişikliğini bildir.
      await widget.onAuthChanged?.call();

      if (!mounted) return;

      // Ardından yeni hesabın profil bilgilerini yükle.
      await _loadProfile();
    }
  }

  // Kayıt ekranını açar.
  Future<void> _openRegisterPage() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(builder: (_) => const RegisterPage()),
    );

    if (!mounted) return;

    if (result == true) {
      // Önce hesap değişikliğini bildir.
      await widget.onAuthChanged?.call();

      if (!mounted) return;

      // Ardından yeni hesabın profil bilgilerini yükle.
      await _loadProfile();
    }
  }

  Future<void> _openEmailVerificationPage() async {
    final authProvider = context.read<AuthProvider>();
    final email = authProvider.email?.trim();

    if (!authProvider.isLoggedIn ||
        authProvider.emailVerified ||
        email == null ||
        email.isEmpty) {
      return;
    }

    await Navigator.push<bool>(
      context,
      MaterialPageRoute(builder: (_) => EmailVerificationPage(email: email)),
    );
  }

  // Şifre belirleme/değiştirme ekranını açar.
  Future<void> _openPasswordManagementPage() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => PasswordManagementPage(
          hasPassword: context.read<AuthProvider>().hasPassword,
        ),
      ),
    );

    if (!mounted) return;

    if (result == true) {
      final authProvider = context.read<AuthProvider>();

      if (!authProvider.isLoggedIn) {
        await widget.onAuthChanged?.call();

        if (!mounted) return;

        await _loadProfile();
        return;
      }

      setState(() {
        _hasPassword = authProvider.hasPassword;
      });
    }
  }

  // Çıkış işlemini onaylatır.
  Future<void> _confirmLogout() async {
    final shouldLogout = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        return AlertDialog(
          title: const Text('Çıkış Yap'),
          content: const Text(
            'Hesabından çıkış yapmak istediğine emin misin? '
            'Misafir kimliğin bu cihazda korunacak.',
          ),
          actions: [
            TextButton(
              onPressed: () {
                Navigator.pop(dialogContext, false);
              },
              child: const Text('Vazgeç'),
            ),
            ElevatedButton(
              onPressed: () {
                Navigator.pop(dialogContext, true);
              },
              child: const Text('Çıkış Yap'),
            ),
          ],
        );
      },
    );

    if (!mounted) return;

    if (shouldLogout == true) {
      await _logout();
    }
  }

  Future<void> _confirmDeleteAccount() async {
    if (_isLoggingOut) return;
    final auth = context.read<AuthProvider>();
    final hasPassword = auth.hasPassword;
    String password = '';
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Hesabı sil?'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Hesabınız ve ilerlemeniz kalıcı olarak silinecek.'),
            if (hasPassword)
              TextField(
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Mevcut şifre'),
                onChanged: (value) => password = value,
              )
            else
              const Text('Google hesabınızı yeniden doğrulamanız gerekiyor.'),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Hesabı sil'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted || _isLoggingOut) return;
    if (hasPassword && password.isEmpty) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('Mevcut şifrenizi girin.')));
      return;
    }
    setState(() => _isLoggingOut = true);
    try {
      final result = await auth.deleteAccount(
        currentPassword: hasPassword ? password : null,
      );
      if (!mounted) return;
      if (result.success) {
        await _loadProfile();
        await widget.onAuthChanged?.call();
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(result.message ?? 'Hesap silinemedi.')),
        );
      }
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Hesap silinemedi. Lütfen tekrar deneyin.'),
        ),
      );
    } finally {
      password = '';
      if (mounted) setState(() => _isLoggingOut = false);
    }
  }

  // JWT oturumunu temizler ve misafir profilini yükler.
  Future<void> _logout() async {
    if (!mounted) return;

    setState(() {
      _isLoggingOut = true;
    });

    try {
      final authProvider = context.read<AuthProvider>();

      await authProvider.logout();

      // MainNavigationPage üzerindeki SignalR bağlantısını yenile.
      await widget.onAuthChanged?.call();

      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Oturum kapatıldı.'),
          backgroundColor: Colors.green,
        ),
      );

      // Çıkış sonrasında misafir profilini yükle.
      await _loadProfile();
    } catch (error) {
      if (!mounted) return;

      if (kDebugMode) {
        debugPrint('Çıkış hatası: $error');
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Çıkış yapılırken bir hata oluştu.'),
          backgroundColor: Colors.red,
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _isLoggingOut = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    context.watch<LeaderboardProvider>();
    final auth = context.watch<AuthProvider>();
    if (!_isLoading &&
        (auth.isLoggedIn != _isLoggedIn || auth.userId != _userId)) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _loadProfile();
      });
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    return Scaffold(
      appBar: AppBar(
        title: const Text(
          'Profilim',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        actions: [
          IconButton(
            onPressed: _isLoading
                ? null
                : () => refreshProfile(forceRefresh: true),
            icon: const Icon(Icons.refresh),
            tooltip: 'Yenile',
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => refreshProfile(forceRefresh: true),
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_errorMessage != null) {
      return _buildErrorState();
    }

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 30),
      children: [
        _buildProfileHeader(),

        if (_isLoggedIn) ...[const SizedBox(height: 16), _buildProgressCard()],

        if (_isLoggedIn) ...[
          const SizedBox(height: 16),
          _buildEmailVerificationCard(),
        ],

        const SizedBox(height: 20),

        if (_isLoggedIn) _buildLoggedInActions() else _buildGuestActions(),

        const SizedBox(height: 24),
        _buildSectionTitle('İstatistikler'),
        const SizedBox(height: 14),
        _buildStatistics(),

        const SizedBox(height: 24),
        _buildSectionTitle('Oyuncu Bilgileri'),
        const SizedBox(height: 14),
        _buildPlayerInformation(),

        if (_isLoggedIn) ...[
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: _linkGoogleAccount,
            icon: const Icon(Icons.link),
            label: const Text('Google hesabını bağla'),
          ),
        ],

        const SizedBox(height: 24),
        _buildInformationCard(),
      ],
    );
  }

  Widget _buildProgressCard() {
    final profile = context.watch<ProfileProvider>();
    if (profile.summary == null && !profile.loading && profile.error == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => profile.refresh());
    }
    final data = profile.summary ?? const <String, dynamic>{};
    final xp = (data['xp'] as num?)?.toInt() ?? 0;
    final level = (data['level'] as num?)?.toInt() ?? 1;
    final unlocked = ((data['achievements'] as List?) ?? const [])
        .where((a) => (a as Map)['unlocked'] == true)
        .length;
    final total = (data['achievementCount'] as num?)?.toInt() ?? 30;
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF101010),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: const Color(0xFFD4AF37)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.workspace_premium, color: Color(0xFFD4AF37)),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  'SEVİYE $level · ${data['rank'] ?? 'Bronze'}',
                  style: const TextStyle(
                    color: Color(0xFFE8C85A),
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
              Text(
                '$xp XP',
                style: const TextStyle(
                  color: Colors.white,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            'Seri ${data['currentStreak'] ?? 0} gün · En iyi ${data['bestStreak'] ?? 0} gün',
            style: const TextStyle(color: Colors.white70),
          ),
          const SizedBox(height: 6),
          Text(
            'Çözülen ${data['solved'] ?? 0} · Kusursuz ${data['perfect'] ?? 0} · Başarım $unlocked/$total',
            style: const TextStyle(color: Colors.white70),
          ),
          if (profile.loading)
            const LinearProgressIndicator(
              color: Color(0xFFD4AF37),
              backgroundColor: Colors.white12,
            ),
          if (profile.error != null)
            Text(
              profile.error!,
              style: const TextStyle(color: Colors.orangeAccent),
            ),
          Wrap(
            spacing: 4,
            children: [
              TextButton.icon(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute(
                    builder: (_) => const ProfileAchievementsPage(),
                  ),
                ),
                icon: const Icon(Icons.military_tech),
                label: const Text('Başarımlar'),
              ),
              TextButton.icon(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const ProfileHistoryPage()),
                ),
                icon: const Icon(Icons.history),
                label: const Text('Geçmiş'),
              ),
              IconButton(
                onPressed: profile.refresh,
                icon: const Icon(Icons.refresh),
                tooltip: 'İlerlemeyi yenile',
              ),
            ],
          ),
        ],
      ),
    );
  }

  @override
  void dispose() {
    _profileGeneration++;
    super.dispose();
  }

  Widget _buildProfileHeader() {
    final displayName = _isLoggedIn ? _displayFullName : 'Satranç Oyuncusu';

    final subtitle = _isLoggedIn ? (_email ?? '') : _displayClientId;

    final emailVerified = context.select<AuthProvider, bool>(
      (provider) =>
          provider.isLoggedIn &&
          provider.userId == _userId &&
          provider.emailVerified,
    );

    return Container(
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(22),
        gradient: const LinearGradient(
          colors: [Color(0xFF161616), Color(0xFF050505)],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
      ),
      child: Column(
        children: [
          Container(
            width: 92,
            height: 92,
            decoration: BoxDecoration(
              color: const Color(0xFFD4AF37).withValues(alpha: 0.15),
              shape: BoxShape.circle,
              border: Border.all(color: const Color(0xFFD4AF37), width: 2),
            ),
            child: Icon(
              emailVerified ? Icons.verified_user : Icons.person,
              size: 52,
              color: const Color(0xFFD4AF37),
            ),
          ),
          const SizedBox(height: 16),
          Text(
            displayName,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 21, fontWeight: FontWeight.bold),
          ),
          const SizedBox(height: 7),
          Text(
            subtitle,
            textAlign: TextAlign.center,
            style: const TextStyle(color: Colors.white60, fontSize: 12),
          ),
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
            decoration: BoxDecoration(
              color: const Color(0xFFB7D7A8).withValues(alpha: 0.15),
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  _isLoggedIn
                      ? (emailVerified ? Icons.verified : Icons.person_outline)
                      : Icons.workspace_premium,
                  size: 16,
                  color: const Color(0xFFD6B46A),
                ),
                const SizedBox(width: 6),
                Text(
                  _isLoggedIn ? 'Kayıtlı oyuncu' : 'Misafir oyuncu',
                  style: const TextStyle(
                    fontSize: 11,
                    color: Color(0xFFB7D7A8),
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildGuestActions() {
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF26382D),
        borderRadius: BorderRadius.circular(17),
        border: Border.all(color: Colors.white10),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Row(
            children: [
              Icon(Icons.info_outline, color: Color(0xFFD6B46A)),
              SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Misafir hesabı kullanıyorsun.',
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          const Text(
            'Hesap oluşturarak puanlarını kalıcı olarak '
            'saklayabilir ve farklı cihazlardan erişebilirsin.',
            style: TextStyle(color: Colors.white70, fontSize: 12, height: 1.5),
          ),
          const SizedBox(height: 18),
          SizedBox(
            height: 48,
            child: ElevatedButton.icon(
              onPressed: _openLoginPage,
              icon: const Icon(Icons.login),
              label: const Text('Giriş Yap'),
            ),
          ),
          const SizedBox(height: 10),
          SizedBox(
            height: 48,
            child: OutlinedButton.icon(
              onPressed: _openRegisterPage,
              icon: const Icon(Icons.person_add_alt_1),
              label: const Text('Kayıt Ol'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildEmailVerificationCard() {
    final authProvider = context.watch<AuthProvider>();

    if (!authProvider.isLoggedIn || authProvider.userId != _userId) {
      return const SizedBox.shrink();
    }

    final emailVerified = authProvider.emailVerified;

    final statusColor = emailVerified
        ? const Color(0xFFB7D7A8)
        : const Color(0xFFD6B46A);

    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(17),
        border: Border.all(color: statusColor.withValues(alpha: 0.25)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(
                emailVerified
                    ? Icons.verified_user_outlined
                    : Icons.mark_email_unread_outlined,
                color: statusColor,
                size: 25,
              ),
              const SizedBox(width: 12),
              const Expanded(
                child: Text(
                  'E-posta doğrulaması',
                  style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Text(
            emailVerified ? 'E-posta doğrulandı' : 'E-posta doğrulanmadı',
            style: TextStyle(
              color: statusColor,
              fontSize: 13,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            emailVerified
                ? 'E-posta adresin başarıyla doğrulandı.'
                : 'Hesap güvenliğini artırmak için '
                      'e-posta adresini doğrulayabilirsin.',
            style: const TextStyle(
              color: Colors.white70,
              fontSize: 12,
              height: 1.5,
            ),
          ),
          if (!emailVerified) ...[
            const SizedBox(height: 16),
            SizedBox(
              height: 46,
              child: ElevatedButton.icon(
                onPressed: _isLoggingOut || authProvider.isLoading
                    ? null
                    : _openEmailVerificationPage,
                icon: const Icon(Icons.email_outlined),
                label: const Text('E-postayı Doğrula'),
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFFD6B46A),
                  foregroundColor: const Color(0xFF101A17),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildLoggedInActions() {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(17),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Row(
            children: [
              Icon(Icons.check_circle, color: Color(0xFFB7D7A8)),
              SizedBox(width: 10),
              Text(
                'Hesabın aktif',
                style: TextStyle(fontWeight: FontWeight.bold),
              ),
            ],
          ),
          const SizedBox(height: 14),
          SizedBox(
            height: 46,
            child: OutlinedButton.icon(
              onPressed: _openPasswordManagementPage,
              icon: const Icon(Icons.lock_outline),
              label: Text(_hasPassword ? 'Şifre Değiştir' : 'Şifre Belirle'),
            ),
          ),
          const SizedBox(height: 10),
          SizedBox(
            height: 46,
            child: OutlinedButton.icon(
              onPressed: _isLoggingOut ? null : _confirmLogout,
              icon: _isLoggingOut
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.logout),
              label: Text(_isLoggingOut ? 'Çıkış yapılıyor...' : 'Çıkış Yap'),
            ),
          ),
          const SizedBox(height: 10),
          TextButton.icon(
            onPressed: _isLoggingOut ? null : _confirmDeleteAccount,
            icon: const Icon(Icons.delete_forever_outlined, color: Colors.red),
            label: const Text(
              'Hesabı Sil',
              style: TextStyle(color: Colors.red),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSectionTitle(String title) {
    return Text(
      title,
      style: const TextStyle(fontSize: 19, fontWeight: FontWeight.bold),
    );
  }

  Widget _buildStatistics() {
    final provider = context.read<LeaderboardProvider>();
    if (_isLoggedIn &&
        provider.leaderboardFor(null, period: 'alltime') == null) {
      final error = provider.errorFor(null, period: 'alltime');
      if (error != null) {
        return TextButton(
          onPressed: () => refreshProfile(forceRefresh: true),
          child: Text('$error — Tekrar dene'),
        );
      }
      return const Center(child: CircularProgressIndicator());
    }
    final totalScore = _profileData?.totalScore ?? 0;
    final solvedCount = _profileData?.solvedCount ?? 0;
    final rank = _profileData?.rank;

    return Column(
      children: [
        Row(
          children: [
            Expanded(
              child: _buildStatisticCard(
                icon: Icons.stars_rounded,
                title: 'Toplam Puan',
                value: totalScore.round().toString(),
                color: const Color(0xFFD6B46A),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: _buildStatisticCard(
                icon: Icons.extension_rounded,
                title: 'Çözülen',
                value: solvedCount.toString(),
                color: const Color(0xFFB7D7A8),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        _buildStatisticCard(
          icon: Icons.leaderboard_rounded,
          title: 'Genel Sıralama',
          value: rank != null && rank > 0 ? '#$rank' : 'Henüz yok',
          color: const Color(0xFF8DB9D9),
          fullWidth: true,
        ),
      ],
    );
  }

  Widget _buildStatisticCard({
    required IconData icon,
    required String title,
    required String value,
    required Color color,
    bool fullWidth = false,
  }) {
    return Container(
      width: fullWidth ? double.infinity : null,
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(17),
        border: Border.all(color: Colors.white10),
      ),
      child: Row(
        children: [
          Container(
            width: 43,
            height: 43,
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(13),
            ),
            child: Icon(icon, color: color, size: 23),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(fontSize: 11, color: Colors.white54),
                ),
                const SizedBox(height: 5),
                Text(
                  value,
                  style: const TextStyle(
                    fontSize: 19,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildPlayerInformation() {
    final countryCode = context.select<AuthProvider, String?>(
      (provider) => provider.countryCode,
    );

    return Container(
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(17),
      ),
      child: Column(
        children: [
          _buildInformationRow(
            icon: Icons.fingerprint,
            title: 'Oyuncu kimliği',
            value: _isLoggedIn ? 'Hesap #${_userId ?? '-'}' : _displayClientId,
          ),
          const Divider(height: 1, color: Colors.white10),
          if (_isLoggedIn) ...[
            _buildInformationRow(
              icon: Icons.badge_outlined,
              title: 'Ad',
              value: _firstName?.trim().isNotEmpty == true
                  ? _firstName!.trim()
                  : '-',
            ),
            const Divider(height: 1, color: Colors.white10),
            _buildInformationRow(
              icon: Icons.badge_outlined,
              title: 'Soyad',
              value: _lastName?.trim().isNotEmpty == true
                  ? _lastName!.trim()
                  : '-',
            ),
            const Divider(height: 1, color: Colors.white10),
            _buildInformationRow(
              icon: Icons.person_outline,
              title: 'Kullanıcı adı',
              value: _username ?? '-',
            ),
            const Divider(height: 1, color: Colors.white10),
            _buildInformationRow(
              icon: Icons.email_outlined,
              title: 'E-posta',
              value: _email ?? '-',
            ),
            const Divider(height: 1, color: Colors.white10),
            _buildInformationRow(
              icon: Icons.public,
              title: 'Hesap ülkesi',
              value: countryCode ?? 'Henüz belirlenmedi',
            ),
            const Divider(height: 1, color: Colors.white10),
          ],
          _buildInformationRow(
            icon: Icons.emoji_events_outlined,
            title: 'Sıralama dönemi',
            value: 'Tüm zamanlar',
          ),
          const Divider(height: 1, color: Colors.white10),
          _buildInformationRow(
            icon: Icons.check_circle_outline,
            title: 'Oyuncu durumu',
            value: _profileData == null
                ? 'Henüz puan kaydı yok'
                : 'Aktif oyuncu',
          ),
        ],
      ),
    );
  }

  Widget _buildInformationRow({
    required IconData icon,
    required String title,
    required String value,
  }) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
      child: Row(
        children: [
          Icon(icon, size: 20, color: const Color(0xFFB7D7A8)),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              title,
              style: const TextStyle(fontSize: 13, color: Colors.white70),
            ),
          ),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.end,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildInformationCard() {
    return Container(
      padding: const EdgeInsets.all(17),
      decoration: BoxDecoration(
        color: const Color(0xFF26382D),
        borderRadius: BorderRadius.circular(16),
      ),
      child: const Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.info_outline, color: Color(0xFFB7D7A8)),
          SizedBox(width: 12),
          Expanded(
            child: Text(
              'Puan ve sıralama bilgilerin, çözdüğün bulmacalardan '
              'sonra sunucu üzerinden güncellenir.',
              style: TextStyle(
                color: Colors.white70,
                fontSize: 12,
                height: 1.6,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildErrorState() {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        const SizedBox(height: 140),
        const Icon(Icons.cloud_off_outlined, size: 52, color: Colors.white54),
        const SizedBox(height: 16),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Text(
            _errorMessage ?? 'Profil bilgileri alınamadı.',
            textAlign: TextAlign.center,
            style: const TextStyle(color: Colors.white70),
          ),
        ),
        const SizedBox(height: 20),
        Center(
          child: ElevatedButton.icon(
            onPressed: _loadProfile,
            icon: const Icon(Icons.refresh),
            label: const Text('Tekrar Dene'),
          ),
        ),
      ],
    );
  }
}
