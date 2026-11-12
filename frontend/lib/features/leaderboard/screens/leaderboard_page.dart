
import 'package:flutter/material.dart';

import '../../../core/storage/client_id_storage.dart';
import '../models/leaderboard_entry.dart';
import '../models/leaderboard_period.dart';
import '../models/leaderboard_response.dart';
import '../services/leaderboard_api_service.dart';

class LeaderboardPage extends StatefulWidget {
  const LeaderboardPage({super.key});

  @override
  State<LeaderboardPage> createState() => LeaderboardPageState();
}

class LeaderboardPageState extends State<LeaderboardPage> {
  final LeaderboardApiService _apiService = LeaderboardApiService();
  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  bool _isLoading = true;
  String? _errorMessage;

  String _selectedPeriod = 'daily';
  String _selectedCountry = 'Global';

  LeaderboardResponse? _leaderboard;

  @override
  void initState() {
    super.initState();
    _loadLeaderboard();
  }

  Future<void> refreshLeaderboard() async {
    if (!mounted || _isLoading) return;

    await _loadLeaderboard();
  }

  Future<void> _loadLeaderboard() async {
    if (!mounted) return;

    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      final response = await _apiService.getLeaderboard(
        clientId: clientId,
        country: _selectedCountry == 'Global' ? null : 'TR',
        limit: 10,
      );

      if (!mounted) return;

      if (!response.success || response.data == null) {
        setState(() {
          _errorMessage =
              response.message ?? 'Sıralama bilgileri alınamadı.';
          _isLoading = false;
        });
        return;
      }

      setState(() {
        _leaderboard = response.data;
        _isLoading = false;
      });
    } catch (error) {
      if (!mounted) return;

      setState(() {
        _errorMessage =
        'Sunucuya bağlanırken bir hata oluştu. Lütfen tekrar dene.';
        _isLoading = false;
      });
    }
  }

  LeaderboardPeriod? get _selectedLeaderboard {
    final data = _leaderboard;

    if (data == null) return null;

    switch (_selectedPeriod) {
      case 'daily':
        return data.daily;
      case 'weekly':
        return data.weekly;
      case 'monthly':
        return data.monthly;
      case 'allTime':
        return data.allTime;
      default:
        return data.daily;
    }
  }

  String get _periodTitle {
    switch (_selectedPeriod) {
      case 'daily':
        return 'Günlük Sıralama';
      case 'weekly':
        return 'Haftalık Sıralama';
      case 'monthly':
        return 'Aylık Sıralama';
      case 'allTime':
        return 'Tüm Zamanlar';
      default:
        return 'Sıralama';
    }
  }

  @override
  Widget build(BuildContext context) {
    final leaderboard = _selectedLeaderboard;

    return Scaffold(
      appBar: AppBar(
        title: const Text(
          'Liderlik Tablosu',
          style: TextStyle(
            fontWeight: FontWeight.bold,
          ),
        ),
        actions: [
          IconButton(
            onPressed: _isLoading ? null : _loadLeaderboard,
            icon: const Icon(Icons.refresh),
            tooltip: 'Yenile',
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _loadLeaderboard,
        child: _buildBody(leaderboard),
      ),
    );
  }

  Widget _buildBody(LeaderboardPeriod? leaderboard) {
    if (_isLoading) {
      return const Center(
        child: CircularProgressIndicator(),
      );
    }

    if (_errorMessage != null) {
      return _buildErrorState();
    }

    if (leaderboard == null) {
      return _buildEmptyState('Sıralama bilgisi bulunamadı.');
    }

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
      children: [
        _buildHeader(),
        const SizedBox(height: 24),
        _buildCountrySelector(),
        const SizedBox(height: 20),
        _buildPeriodSelector(),
        const SizedBox(height: 24),
        _buildSectionTitle(
          _periodTitle,
          'En başarılı oyuncular',
        ),
        const SizedBox(height: 18),
        if (leaderboard.rankings.isEmpty)
          _buildEmptyState('Bu dönemde henüz sıralama yok.')
        else ...[
          _buildPodium(leaderboard.rankings),
          const SizedBox(height: 24),
          _buildRankingList(leaderboard.rankings),
        ],
        if (leaderboard.currentUser != null) ...[
          const SizedBox(height: 24),
          _buildCurrentUserCard(leaderboard.currentUser!),
        ],
        const SizedBox(height: 16),
      ],
    );
  }

  Widget _buildHeader() {
    return Container(
      padding: const EdgeInsets.all(22),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(22),
        gradient: const LinearGradient(
          colors: [
            Color(0xFF263D32),
            Color(0xFF192820),
          ],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: const [
                Text(
                  'Zirveye oyna!',
                  style: TextStyle(
                    fontSize: 23,
                    fontWeight: FontWeight.bold,
                    color: Colors.white,
                  ),
                ),
                SizedBox(height: 8),
                Text(
                  'Puanını yükselt, diğer oyuncularla yarış.',
                  style: TextStyle(
                    fontSize: 13,
                    color: Colors.white70,
                    height: 1.5,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          const Icon(
            Icons.emoji_events_rounded,
            size: 54,
            color: Color(0xFFD6B46A),
          ),
        ],
      ),
    );
  }

  Widget _buildCountrySelector() {
    return Row(
      children: [
        const Text(
          'Bölge',
          style: TextStyle(
            fontWeight: FontWeight.w600,
            fontSize: 14,
          ),
        ),
        const Spacer(),
        _buildFilterButton('Global'),
        const SizedBox(width: 8),
        _buildFilterButton('Türkiye'),
      ],
    );
  }

  Widget _buildFilterButton(String country) {
    final isSelected = _selectedCountry == country;

    return ChoiceChip(
      label: Text(country),
      selected: isSelected,
      onSelected: _isLoading
          ? null
          : (selected) {
        if (!selected || _selectedCountry == country) return;

        setState(() {
          _selectedCountry = country;
        });

        _loadLeaderboard();
      },
      selectedColor: const Color(0xFFB7D7A8),
      backgroundColor: const Color(0xFF1B2923),
      labelStyle: TextStyle(
        color: isSelected
            ? const Color(0xFF101A17)
            : Colors.white70,
        fontWeight: FontWeight.w600,
      ),
      side: BorderSide.none,
      showCheckmark: false,
    );
  }

  Widget _buildPeriodSelector() {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: SegmentedButton<String>(
        segments: const [
          ButtonSegment(
            value: 'daily',
            label: Text('Günlük'),
          ),
          ButtonSegment(
            value: 'weekly',
            label: Text('Haftalık'),
          ),
          ButtonSegment(
            value: 'monthly',
            label: Text('Aylık'),
          ),
          ButtonSegment(
            value: 'allTime',
            label: Text('Tümü'),
          ),
        ],
        selected: {_selectedPeriod},
        onSelectionChanged: (selection) {
          setState(() {
            _selectedPeriod = selection.first;
          });
        },
        showSelectedIcon: false,
        style: ButtonStyle(
          visualDensity: VisualDensity.compact,
          backgroundColor: WidgetStateProperty.resolveWith((states) {
            if (states.contains(WidgetState.selected)) {
              return const Color(0xFFB7D7A8);
            }

            return const Color(0xFF1B2923);
          }),
          foregroundColor: WidgetStateProperty.resolveWith((states) {
            if (states.contains(WidgetState.selected)) {
              return const Color(0xFF101A17);
            }

            return Colors.white70;
          }),
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
            fontSize: 20,
            fontWeight: FontWeight.bold,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          subtitle,
          style: const TextStyle(
            color: Colors.white54,
            fontSize: 12,
          ),
        ),
      ],
    );
  }

  Widget _buildPodium(List<LeaderboardEntry> rankings) {
    final topThree = rankings
        .where((entry) => entry.rank >= 1 && entry.rank <= 3)
        .toList()
      ..sort((a, b) => a.rank.compareTo(b.rank));

    if (topThree.isEmpty) {
      return const SizedBox.shrink();
    }

    LeaderboardEntry? entryForRank(int rank) {
      for (final entry in topThree) {
        if (entry.rank == rank) return entry;
      }

      return null;
    }

    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Expanded(
          child: _buildPodiumItem(
            entryForRank(2),
            height: 155,
            medal: '🥈',
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _buildPodiumItem(
            entryForRank(1),
            height: 180,
            medal: '🥇',
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _buildPodiumItem(
            entryForRank(3),
            height: 155,
            medal: '🥉',
          ),
        ),
      ],
    );
  }

  Widget _buildPodiumItem(
      LeaderboardEntry? entry, {
        required double height,
        required String medal,
      }) {
    if (entry == null) {
      return SizedBox(height: height);
    }

    final isFirst = entry.rank == 1;

    return Container(
      height: height,
      padding: const EdgeInsets.symmetric(
        horizontal: 5,
        vertical: 8,
      ),
      decoration: BoxDecoration(
        color: isFirst
            ? const Color(0xFF354A39)
            : const Color(0xFF1B2923),
        borderRadius: const BorderRadius.vertical(
          top: Radius.circular(18),
          bottom: Radius.circular(10),
        ),
        border: Border.all(
          color: isFirst
              ? const Color(0xFFD6B46A)
              : Colors.white10,
        ),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            medal,
            style: const TextStyle(fontSize: 26),
          ),
          const SizedBox(height: 4),
          Text(
            '#${entry.rank}',
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.bold,
              color: isFirst
                  ? const Color(0xFFD6B46A)
                  : Colors.white70,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            _shortClientId(entry.clientId),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 4),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(
              '${entry.totalScore} puan',
              style: const TextStyle(
                fontSize: 12,
                color: Color(0xFFB7D7A8),
                fontWeight: FontWeight.bold,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildRankingList(List<LeaderboardEntry> rankings) {
    final remainingEntries = rankings
        .where((entry) => entry.rank > 3)
        .toList();

    if (remainingEntries.isEmpty) {
      return const SizedBox.shrink();
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Diğer Oyuncular',
          style: TextStyle(
            fontSize: 17,
            fontWeight: FontWeight.bold,
          ),
        ),
        const SizedBox(height: 12),
        ...remainingEntries.map(
              (entry) => _buildRankingTile(entry),
        ),
      ],
    );
  }

  Widget _buildRankingTile(LeaderboardEntry entry) {
    final currentUser = _selectedLeaderboard?.currentUser;
    final isCurrentUser = currentUser?.clientId == entry.clientId;

    return Container(
      margin: const EdgeInsets.only(bottom: 9),
      padding: const EdgeInsets.symmetric(
        horizontal: 14,
        vertical: 14,
      ),
      decoration: BoxDecoration(
        color: isCurrentUser
            ? const Color(0xFF293D30)
            : const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: isCurrentUser
              ? const Color(0xFFB7D7A8)
              : Colors.white10,
        ),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 34,
            child: Text(
              '#${entry.rank}',
              style: const TextStyle(
                fontWeight: FontWeight.bold,
                color: Color(0xFFD6B46A),
              ),
            ),
          ),
          const CircleAvatar(
            radius: 17,
            backgroundColor: Color(0xFF304438),
            child: Icon(
              Icons.person,
              size: 19,
              color: Color(0xFFB7D7A8),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _shortClientId(entry.clientId),
                  style: const TextStyle(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  '${entry.solvedCount} bulmaca çözdü',
                  style: const TextStyle(
                    fontSize: 11,
                    color: Colors.white54,
                  ),
                ),
              ],
            ),
          ),
          Text(
            '${entry.totalScore}',
            style: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: Color(0xFFB7D7A8),
            ),
          ),
          const SizedBox(width: 4),
          const Icon(
            Icons.star_rounded,
            size: 17,
            color: Color(0xFFD6B46A),
          ),
        ],
      ),
    );
  }

  Widget _buildCurrentUserCard(LeaderboardEntry entry) {
    final isAlreadyInList =
        _selectedLeaderboard?.rankings.any(
              (item) => item.clientId == entry.clientId,
        ) ??
            false;

    if (isAlreadyInList) {
      return const SizedBox.shrink();
    }

    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF293D30),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(
          color: const Color(0xFFB7D7A8),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(
                Icons.person_pin_circle,
                color: Color(0xFFB7D7A8),
              ),
              SizedBox(width: 8),
              Text(
                'Senin Sıralaman',
                style: TextStyle(
                  fontWeight: FontWeight.bold,
                  fontSize: 15,
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              Text(
                '#${entry.rank}',
                style: const TextStyle(
                  fontSize: 25,
                  fontWeight: FontWeight.bold,
                  color: Color(0xFFD6B46A),
                ),
              ),
              const Spacer(),
              Column(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    '${entry.totalScore} puan',
                    style: const TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 17,
                      color: Color(0xFFB7D7A8),
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${entry.solvedCount} çözülen bulmaca',
                    style: const TextStyle(
                      fontSize: 11,
                      color: Colors.white70,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildEmptyState(String message) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(
        horizontal: 20,
        vertical: 36,
      ),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(18),
      ),
      child: Column(
        children: [
          const Icon(
            Icons.leaderboard_outlined,
            size: 42,
            color: Color(0xFFB7D7A8),
          ),
          const SizedBox(height: 12),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: Colors.white70,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildErrorState() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.cloud_off_outlined,
              size: 52,
              color: Colors.white54,
            ),
            const SizedBox(height: 16),
            Text(
              _errorMessage ?? 'Bir hata oluştu.',
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white70,
              ),
            ),
            const SizedBox(height: 20),
            ElevatedButton.icon(
              onPressed: _loadLeaderboard,
              icon: const Icon(Icons.refresh),
              label: const Text('Tekrar Dene'),
            ),
          ],
        ),
      ),
    );
  }

  String _shortClientId(String clientId) {
    if (clientId.isEmpty) return 'Oyuncu';

    if (clientId.length <= 10) return clientId;

    return '${clientId.substring(0, 6)}...';
  }
}
