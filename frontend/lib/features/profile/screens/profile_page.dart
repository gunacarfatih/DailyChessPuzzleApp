
import 'package:flutter/material.dart';

import '../../../core/storage/auth_storage.dart';
import '../../../core/storage/client_id_storage.dart';
import '../../auth/screens/login_page.dart';
import '../../auth/screens/register_page.dart';
import '../../auth/services/auth_api_service.dart';
import '../../leaderboard/models/leaderboard_entry.dart';
import '../../leaderboard/services/leaderboard_api_service.dart';
import 'password_management_page.dart';

class ProfilePage extends StatefulWidget {
  const ProfilePage({super.key});

  @override
  State<ProfilePage> createState() => ProfilePageState();
}

class ProfilePageState extends State<ProfilePage> {
  final LeaderboardApiService _leaderboardApiService =
  LeaderboardApiService();

  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  final AuthStorage _authStorage = AuthStorage.instance;

  final AuthApiService _authService = AuthApiService();

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

  LeaderboardEntry? _profileData;

  @override
  void initState() {
    super.initState();
    _loadProfile();
  }

  Future<void> refreshProfile() async {
    if (!mounted || _isLoading || _isLoggingOut) return;

    await _loadProfile();
  }

  // Oturum ve profil bilgilerini yükler.
  Future<void> _loadProfile() async {
    if (mounted) {
      setState(() {
        _isLoading = true;
        _errorMessage = null;
      });
    }

    try {
      final clientId = await _clientIdStorage.getClientId();

      final isLoggedIn = await _authStorage.isLoggedIn();

      String? username;
      String? email;
      String? firstName;
      String? lastName;
      int? userId;
      bool hasPassword = false;

      if (isLoggedIn) {
        username = await _authStorage.getUsername();
        email = await _authStorage.getEmail();
        firstName = await _authStorage.getFirstName();
        lastName = await _authStorage.getLastName();
        userId = await _authStorage.getUserId();
        hasPassword = await _authStorage.getHasPassword();
      }

      final response = await _leaderboardApiService.getLeaderboard(
        clientId: clientId,
        limit: 10,
      );

      if (!mounted) return;

      if (!response.success || response.data == null) {
        setState(() {
          _clientId = clientId;
          _isLoggedIn = isLoggedIn;
          _username = username;
          _email = email;
          _firstName = firstName;
          _lastName = lastName;
          _userId = userId;
          _hasPassword = hasPassword;
          _errorMessage =
              response.message ?? 'Profil bilgileri alınamadı.';
          _isLoading = false;
        });

        return;
      }

      final allTime = response.data!.allTime;

      final LeaderboardEntry? currentUser;

      if (allTime.currentUser != null) {
        currentUser = allTime.currentUser;
      } else if (isLoggedIn && userId != null) {
        currentUser = _findCurrentUser(
          allTime.rankings,
          'user:$userId',
        );
      } else if (!isLoggedIn) {
        currentUser = _findCurrentUser(
          allTime.rankings,
          clientId,
        );
      } else {
        currentUser = null;
      }

      setState(() {
        _clientId = clientId;
        _isLoggedIn = isLoggedIn;
        _username = username;
        _email = email;
        _firstName = firstName;
        _lastName = lastName;
        _userId = userId;
        _hasPassword = hasPassword;
        _profileData = currentUser;
        _isLoading = false;
      });
    } catch (error) {
      if (!mounted) return;

      setState(() {
        _errorMessage =
        'Profil bilgileri alınırken bir hata oluştu.';
        _isLoading = false;
      });
    }
  }

  LeaderboardEntry? _findCurrentUser(
      List<LeaderboardEntry> rankings,
      String clientId,
      ) {
    for (final entry in rankings) {
      if (entry.clientId == clientId) {
        return entry;
      }
    }

    return null;
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
      MaterialPageRoute(
        builder: (_) => const LoginPage(),
      ),
    );

    if (!mounted) return;

    if (result == true) {
      await _loadProfile();
    }
  }

  // Kayıt ekranını açar.
  Future<void> _openRegisterPage() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => const RegisterPage(),
      ),
    );

    if (!mounted) return;

    if (result == true) {
      await _loadProfile();
    }
  }

  // Şifre belirleme/değiştirme ekranını açar.
  Future<void> _openPasswordManagementPage() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => PasswordManagementPage(
          hasPassword: _hasPassword,
        ),
      ),
    );

    if (!mounted) return;

    if (result == true) {
      setState(() {
        _hasPassword = true;
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

    if (shouldLogout == true) {
      await _logout();
    }
  }

  // JWT oturumunu temizler ve misafir profilini yükler.
  Future<void> _logout() async {
    setState(() {
      _isLoggingOut = true;
    });

    try {
      await _authService.logout();

      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Oturum kapatıldı.'),
          backgroundColor: Colors.green,
        ),
      );

      await _loadProfile();
    } catch (error) {
      if (!mounted) return;

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
    return Scaffold(
      appBar: AppBar(
        title: const Text(
          'Profilim',
          style: TextStyle(
            fontWeight: FontWeight.bold,
          ),
        ),
        actions: [
          IconButton(
            onPressed: _isLoading ? null : _loadProfile,
            icon: const Icon(Icons.refresh),
            tooltip: 'Yenile',
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _loadProfile,
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) {
      return const Center(
        child: CircularProgressIndicator(),
      );
    }

    if (_errorMessage != null) {
      return _buildErrorState();
    }

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 30),
      children: [
        _buildProfileHeader(),
        const SizedBox(height: 20),
        if (_isLoggedIn)
          _buildLoggedInActions()
        else
          _buildGuestActions(),
        const SizedBox(height: 24),
        _buildSectionTitle('İstatistikler'),
        const SizedBox(height: 14),
        _buildStatistics(),
        const SizedBox(height: 24),
        _buildSectionTitle('Oyuncu Bilgileri'),
        const SizedBox(height: 14),
        _buildPlayerInformation(),
        const SizedBox(height: 24),
        _buildInformationCard(),
      ],
    );
  }

  Widget _buildProfileHeader() {
    final displayName = _isLoggedIn
        ? _displayFullName
        : 'Satranç Oyuncusu';

    final subtitle = _isLoggedIn
        ? (_email ?? '')
        : _displayClientId;

    return Container(
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(22),
        gradient: const LinearGradient(
          colors: [
            Color(0xFF304438),
            Color(0xFF1B2923),
          ],
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
              color: const Color(0xFFB7D7A8)
                  .withValues(alpha: 0.15),
              shape: BoxShape.circle,
              border: Border.all(
                color: const Color(0xFFB7D7A8),
                width: 2,
              ),
            ),
            child: Icon(
              _isLoggedIn ? Icons.verified_user : Icons.person,
              size: 52,
              color: const Color(0xFFB7D7A8),
            ),
          ),
          const SizedBox(height: 16),
          Text(
            displayName,
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 21,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 7),
          Text(
            subtitle,
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: Colors.white60,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: 14,
              vertical: 7,
            ),
            decoration: BoxDecoration(
              color: const Color(0xFFB7D7A8)
                  .withValues(alpha: 0.15),
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  _isLoggedIn
                      ? Icons.verified
                      : Icons.workspace_premium,
                  size: 16,
                  color: const Color(0xFFD6B46A),
                ),
                const SizedBox(width: 6),
                Text(
                  _isLoggedIn
                      ? 'Kayıtlı oyuncu'
                      : 'Misafir oyuncu',
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
        border: Border.all(
          color: Colors.white10,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Row(
            children: [
              Icon(
                Icons.info_outline,
                color: Color(0xFFD6B46A),
              ),
              SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Misafir hesabı kullanıyorsun.',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          const Text(
            'Hesap oluşturarak puanlarını kalıcı olarak '
                'saklayabilir ve farklı cihazlardan erişebilirsin.',
            style: TextStyle(
              color: Colors.white70,
              fontSize: 12,
              height: 1.5,
            ),
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
              Icon(
                Icons.check_circle,
                color: Color(0xFFB7D7A8),
              ),
              SizedBox(width: 10),
              Text(
                'Hesabın aktif',
                style: TextStyle(
                  fontWeight: FontWeight.bold,
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),

          SizedBox(
            height: 46,
            child: OutlinedButton.icon(
              onPressed: _openPasswordManagementPage,
              icon: const Icon(Icons.lock_outline),
              label: Text(
                _hasPassword
                    ? 'Şifre Değiştir'
                    : 'Şifre Belirle',
              ),
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
                child: CircularProgressIndicator(
                  strokeWidth: 2,
                ),
              )
                  : const Icon(Icons.logout),
              label: Text(
                _isLoggingOut
                    ? 'Çıkış yapılıyor...'
                    : 'Çıkış Yap',
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSectionTitle(String title) {
    return Text(
      title,
      style: const TextStyle(
        fontSize: 19,
        fontWeight: FontWeight.bold,
      ),
    );
  }

  Widget _buildStatistics() {
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
                value: totalScore.toString(),
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
          value: rank != null && rank > 0
              ? '#$rank'
              : 'Henüz yok',
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
        border: Border.all(
          color: Colors.white10,
        ),
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
            child: Icon(
              icon,
              color: color,
              size: 23,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                    fontSize: 11,
                    color: Colors.white54,
                  ),
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
            value: _isLoggedIn
                ? 'Hesap #${_userId ?? '-'}'
                : _displayClientId,
          ),
          const Divider(
            height: 1,
            color: Colors.white10,
          ),
          if (_isLoggedIn) ...[
            _buildInformationRow(
              icon: Icons.badge_outlined,
              title: 'Ad',
              value: _firstName?.trim().isNotEmpty == true
                  ? _firstName!.trim()
                  : '-',
            ),
            const Divider(
              height: 1,
              color: Colors.white10,
            ),
            _buildInformationRow(
              icon: Icons.badge_outlined,
              title: 'Soyad',
              value: _lastName?.trim().isNotEmpty == true
                  ? _lastName!.trim()
                  : '-',
            ),
            const Divider(
              height: 1,
              color: Colors.white10,
            ),
            _buildInformationRow(
              icon: Icons.person_outline,
              title: 'Kullanıcı adı',
              value: _username ?? '-',
            ),
            const Divider(
              height: 1,
              color: Colors.white10,
            ),
            _buildInformationRow(
              icon: Icons.email_outlined,
              title: 'E-posta',
              value: _email ?? '-',
            ),
            const Divider(
              height: 1,
              color: Colors.white10,
            ),
          ],
          _buildInformationRow(
            icon: Icons.emoji_events_outlined,
            title: 'Sıralama dönemi',
            value: 'Tüm zamanlar',
          ),
          const Divider(
            height: 1,
            color: Colors.white10,
          ),
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
      padding: const EdgeInsets.symmetric(
        horizontal: 16,
        vertical: 17,
      ),
      child: Row(
        children: [
          Icon(
            icon,
            size: 20,
            color: const Color(0xFFB7D7A8),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              title,
              style: const TextStyle(
                fontSize: 13,
                color: Colors.white70,
              ),
            ),
          ),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.end,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
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
          Icon(
            Icons.info_outline,
            color: Color(0xFFB7D7A8),
          ),
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
        const Icon(
          Icons.cloud_off_outlined,
          size: 52,
          color: Colors.white54,
        ),
        const SizedBox(height: 16),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Text(
            _errorMessage ?? 'Profil bilgileri alınamadı.',
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: Colors.white70,
            ),
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
