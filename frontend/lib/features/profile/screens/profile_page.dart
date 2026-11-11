
import 'package:flutter/material.dart';

import '../../../core/storage/client_id_storage.dart';
import '../../leaderboard/models/leaderboard_entry.dart';
import '../../leaderboard/services/leaderboard_api_service.dart';

class ProfilePage extends StatefulWidget {
  const ProfilePage({super.key});

  @override
  State<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends State<ProfilePage> {
  final LeaderboardApiService _leaderboardApiService =
  LeaderboardApiService();

  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  bool _isLoading = true;
  String? _errorMessage;

  String _clientId = '';
  LeaderboardEntry? _profileData;

  @override
  void initState() {
    super.initState();
    _loadProfile();
  }

  Future<void> _loadProfile() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      final response = await _leaderboardApiService.getLeaderboard(
        clientId: clientId,
        limit: 10,
      );

      if (!mounted) return;

      if (!response.success || response.data == null) {
        setState(() {
          _errorMessage =
              response.message ?? 'Profil bilgileri alınamadı.';
          _isLoading = false;
        });
        return;
      }

      final allTime = response.data!.allTime;

      // Öncelikle backend'in doğrudan döndürdüğü
      // CurrentUser bilgisini kullanıyoruz.
      // Bulunamazsa ilk sıralama listesinden arıyoruz.
      final currentUser = allTime.currentUser ??
          _findCurrentUser(allTime.rankings, clientId);

      setState(() {
        _clientId = clientId;
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

  String get _displayClientId {
    if (_clientId.isEmpty) return 'Oyuncu';

    if (_clientId.length <= 12) return _clientId;

    return '${_clientId.substring(0, 8)}...';
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
              color: const Color(0xFFB7D7A8).withValues(alpha: 0.15),
              shape: BoxShape.circle,
              border: Border.all(
                color: const Color(0xFFB7D7A8),
                width: 2,
              ),
            ),
            child: const Icon(
              Icons.person,
              size: 52,
              color: Color(0xFFB7D7A8),
            ),
          ),
          const SizedBox(height: 16),
          const Text(
            'Satranç Oyuncusu',
            style: TextStyle(
              fontSize: 21,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 7),
          Text(
            _displayClientId,
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
              color: const Color(0xFFB7D7A8).withValues(alpha: 0.15),
              borderRadius: BorderRadius.circular(20),
            ),
            child: const Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.workspace_premium,
                  size: 16,
                  color: Color(0xFFD6B46A),
                ),
                SizedBox(width: 6),
                Text(
                  'Satranç yolculuğun devam ediyor',
                  style: TextStyle(
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
            value: _displayClientId,
          ),
          const Divider(
            height: 1,
            color: Colors.white10,
          ),
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
