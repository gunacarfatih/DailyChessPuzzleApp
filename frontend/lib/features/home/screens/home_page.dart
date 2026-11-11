
import 'package:flutter/material.dart';

import '../../../core/storage/client_id_storage.dart';
import '../../leaderboard/services/leaderboard_api_service.dart';

class HomePage extends StatefulWidget {
  final VoidCallback onOpenPuzzle;
  final VoidCallback onOpenLeaderboard;

  const HomePage({
    super.key,
    required this.onOpenPuzzle,
    required this.onOpenLeaderboard,
  });

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  final ClientIdStorage _clientIdStorage = ClientIdStorage();
  final LeaderboardApiService _leaderboardApiService =
  LeaderboardApiService();

  bool _isLoadingStats = true;

  int _totalScore = 0;
  int _solvedCount = 0;
  int? _rank;

  @override
  void initState() {
    super.initState();
    _loadStatistics();
  }

  Future<void> _loadStatistics() async {
    try {
      final clientId = await _clientIdStorage.getClientId();

      final response = await _leaderboardApiService.getLeaderboard(
        clientId: clientId,
        limit: 10,
      );

      if (!mounted) return;

      if (response.success && response.data != null) {
        final user = response.data!.allTime.currentUser;

        setState(() {
          _totalScore = user?.totalScore ?? 0;
          _solvedCount = user?.solvedCount ?? 0;
          _rank = user?.rank;
          _isLoadingStats = false;
        });
      } else {
        setState(() {
          _isLoadingStats = false;
        });
      }
    } catch (_) {
      if (!mounted) return;

      setState(() {
        _isLoadingStats = false;
      });
    }
  }

  String get _formattedDate {
    const months = [
      'Ocak',
      'Şubat',
      'Mart',
      'Nisan',
      'Mayıs',
      'Haziran',
      'Temmuz',
      'Ağustos',
      'Eylül',
      'Ekim',
      'Kasım',
      'Aralık',
    ];

    final now = DateTime.now();

    return '${now.day} ${months[now.month - 1]} ${now.year}';
  }

  Future<void> _refresh() async {
    await _loadStatistics();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Row(
          children: [
            Icon(
              Icons.grid_view_rounded,
              color: Color(0xFFB7D7A8),
              size: 23,
            ),
            SizedBox(width: 9),
            Text(
              'Daily Chess Puzzle',
              style: TextStyle(
                fontWeight: FontWeight.bold,
                fontSize: 19,
              ),
            ),
          ],
        ),
        actions: [
          IconButton(
            onPressed: _refresh,
            icon: const Icon(Icons.refresh),
            tooltip: 'İstatistikleri yenile',
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
          children: [
            _buildWelcomeSection(),
            const SizedBox(height: 26),
            _buildDailyPuzzleCard(),
            const SizedBox(height: 28),
            _buildSectionTitle(
              'İstatistiklerin',
              'Satranç yolculuğundaki ilerlemen',
            ),
            const SizedBox(height: 14),
            _buildStatistics(),
            const SizedBox(height: 28),
            _buildLeaderboardCard(),
            const SizedBox(height: 20),
            _buildDailyTip(),
          ],
        ),
      ),
    );
  }

  Widget _buildWelcomeSection() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          _formattedDate,
          style: const TextStyle(
            color: Color(0xFFB7D7A8),
            fontSize: 13,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: 10),
        const Text(
          'Satranç dünyasına\nhoş geldin!',
          style: TextStyle(
            fontSize: 29,
            height: 1.2,
            fontWeight: FontWeight.bold,
            letterSpacing: -0.5,
          ),
        ),
        const SizedBox(height: 9),
        const Text(
          'Bugün kendini geliştir. Her hamle seni daha iyi bir oyuncu yapar.',
          style: TextStyle(
            color: Colors.white60,
            fontSize: 13,
            height: 1.6,
          ),
        ),
      ],
    );
  }

  Widget _buildDailyPuzzleCard() {
    return InkWell(
      onTap: widget.onOpenPuzzle,
      borderRadius: BorderRadius.circular(22),
      child: Container(
        padding: const EdgeInsets.all(20),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(22),
          gradient: const LinearGradient(
            colors: [
              Color(0xFF354B3B),
              Color(0xFF1B2923),
            ],
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
          ),
          border: Border.all(
            color: const Color(0xFFB7D7A8).withValues(alpha: 0.2),
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 11,
                    vertical: 7,
                  ),
                  decoration: BoxDecoration(
                    color: const Color(0xFFB7D7A8).withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: const Text(
                    'GÜNÜN BULMACASI',
                    style: TextStyle(
                      fontSize: 10,
                      color: Color(0xFFB7D7A8),
                      fontWeight: FontWeight.bold,
                      letterSpacing: 1,
                    ),
                  ),
                ),
                const Spacer(),
                const Icon(
                  Icons.arrow_outward_rounded,
                  color: Color(0xFFB7D7A8),
                ),
              ],
            ),
            const SizedBox(height: 22),
            Row(
              children: [
                Expanded(
                  flex: 3,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text(
                        'Hamleni bul!',
                        style: TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      const SizedBox(height: 9),
                      const Text(
                        'Bugünün satranç problemini çöz ve puanını kazan.',
                        style: TextStyle(
                          fontSize: 12,
                          color: Colors.white70,
                          height: 1.5,
                        ),
                      ),
                      const SizedBox(height: 20),
                      ElevatedButton(
                        onPressed: widget.onOpenPuzzle,
                        style: ElevatedButton.styleFrom(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 18,
                            vertical: 12,
                          ),
                        ),
                        child: const Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(
                              'Bulmacaya Başla',
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                            SizedBox(width: 7),
                            Icon(
                              Icons.arrow_forward_rounded,
                              size: 17,
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                const Expanded(
                  flex: 2,
                  child: _ChessDecoration(),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildSectionTitle(String title, String subtitle) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: const TextStyle(
            fontSize: 19,
            fontWeight: FontWeight.bold,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          subtitle,
          style: const TextStyle(
            fontSize: 12,
            color: Colors.white54,
          ),
        ),
      ],
    );
  }

  Widget _buildStatistics() {
    return Row(
      children: [
        Expanded(
          child: _buildStatisticCard(
            icon: Icons.stars_rounded,
            title: 'Toplam Puan',
            value: _isLoadingStats ? '...' : '$_totalScore',
            color: const Color(0xFFD6B46A),
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: _buildStatisticCard(
            icon: Icons.extension_rounded,
            title: 'Çözülen',
            value: _isLoadingStats ? '...' : '$_solvedCount',
            color: const Color(0xFFB7D7A8),
          ),
        ),
      ],
    );
  }

  Widget _buildStatisticCard({
    required IconData icon,
    required String title,
    required String value,
    required Color color,
  }) {
    return Container(
      padding: const EdgeInsets.all(17),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(17),
        border: Border.all(
          color: Colors.white10,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            icon,
            color: color,
            size: 25,
          ),
          const SizedBox(height: 13),
          Text(
            value,
            style: const TextStyle(
              fontSize: 23,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 5),
          Text(
            title,
            style: const TextStyle(
              color: Colors.white54,
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildLeaderboardCard() {
    return InkWell(
      onTap: widget.onOpenLeaderboard,
      borderRadius: BorderRadius.circular(18),
      child: Container(
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(
          color: const Color(0xFF1B2923),
          borderRadius: BorderRadius.circular(18),
          border: Border.all(
            color: Colors.white10,
          ),
        ),
        child: Row(
          children: [
            Container(
              width: 48,
              height: 48,
              decoration: BoxDecoration(
                color: const Color(0xFFD6B46A).withValues(alpha: 0.13),
                borderRadius: BorderRadius.circular(14),
              ),
              child: const Icon(
                Icons.emoji_events_rounded,
                color: Color(0xFFD6B46A),
                size: 26,
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    'Liderlik Tablosu',
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 15,
                    ),
                  ),
                  const SizedBox(height: 5),
                  Text(
                    _isLoadingStats
                        ? 'Sıralaman yükleniyor...'
                        : _rank != null
                        ? 'Genel sıralaman: #$_rank'
                        : 'Diğer oyuncularla yarış!',
                    style: const TextStyle(
                      color: Colors.white54,
                      fontSize: 11,
                    ),
                  ),
                ],
              ),
            ),
            const Icon(
              Icons.chevron_right_rounded,
              color: Color(0xFFB7D7A8),
              size: 26,
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDailyTip() {
    return Container(
      padding: const EdgeInsets.all(17),
      decoration: BoxDecoration(
        color: const Color(0xFF26382D),
        borderRadius: BorderRadius.circular(17),
      ),
      child: const Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.lightbulb_outline_rounded,
            color: Color(0xFFD6B46A),
            size: 23,
          ),
          SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Günün tavsiyesi',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 13,
                  ),
                ),
                SizedBox(height: 6),
                Text(
                  'Hamle yapmadan önce rakibinin tehditlerini '
                      've kendi taşlarının güvenliğini kontrol et.',
                  style: TextStyle(
                    color: Colors.white70,
                    fontSize: 12,
                    height: 1.5,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ChessDecoration extends StatelessWidget {
  const _ChessDecoration();

  @override
  Widget build(BuildContext context) {
    const lightSquare = Color(0xFFB7D7A8);
    const darkSquare = Color(0xFF536F59);

    return AspectRatio(
      aspectRatio: 1,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(12),
        child: Column(
          children: List.generate(4, (row) {
            return Expanded(
              child: Row(
                children: List.generate(4, (column) {
                  final isLight = (row + column).isEven;

                  return Expanded(
                    child: Container(
                      color: isLight ? lightSquare : darkSquare,
                      alignment: Alignment.center,
                      child: _pieceForSquare(row, column),
                    ),
                  );
                }),
              ),
            );
          }),
        ),
      ),
    );
  }

  Widget _pieceForSquare(int row, int column) {
    const pieces = {
      '0-1': '♞',
      '1-2': '♟',
      '2-1': '♙',
      '3-2': '♘',
    };

    final piece = pieces['$row-$column'];

    if (piece == null) {
      return const SizedBox.shrink();
    }

    final isWhite = piece == '♙' || piece == '♘';

    return Text(
      piece,
      style: TextStyle(
        fontSize: 30,
        color: isWhite ? Colors.white : const Color(0xFF17231C),
        shadows: const [
          Shadow(
            color: Colors.black26,
            blurRadius: 3,
            offset: Offset(1, 2),
          ),
        ],
      ),
    );
  }
}
