
import 'package:flutter/material.dart';

import '../../../core/storage/client_id_storage.dart';
import '../../../core/services/location_service.dart';
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
  final LocationService _locationService = LocationService();

  bool _isLoading = true;
  String? _errorMessage;

  String _selectedPeriod = 'daily';

  String _selectedCountry = 'Global';
  String? _detectedCountryCode;
  String? _detectedCountryName;

  LeaderboardResponse? _leaderboard;

  @override
  void initState() {
    super.initState();
    _initializeLocationAndLoadLeaderboard();
  }

  Future<void> _initializeLocationAndLoadLeaderboard() async {
    final countryInfo = await _locationService.getCountryInfo();

    if (!mounted) return;

    if (countryInfo != null) {
      setState(() {
        _detectedCountryCode = countryInfo.code;
        _detectedCountryName = countryInfo.name;
      });
    }

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
        country: _selectedCountry == 'Global'
            ? null
            : _detectedCountryCode,
        limit: 10,
      );

      if (!mounted) return;

      final data = response.data;

      if (!response.success || data == null) {
        setState(() {
          _errorMessage = response.message;
          _isLoading = false;
        });
        return;
      }

      setState(() {
        _leaderboard = data;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;

      setState(() {
        _errorMessage = e.toString();
        _isLoading = false;
      });
    }
  }

  Future<void> refreshLeaderboard() async {
    if (!mounted || _isLoading) return;
    await _loadLeaderboard();
  }

  LeaderboardPeriod? get _selectedLeaderboard {
    final leaderboard = _leaderboard;

    if (leaderboard == null) return null;

    switch (_selectedPeriod) {
      case 'daily':
        return leaderboard.daily;
      case 'weekly':
        return leaderboard.weekly;
      case 'monthly':
        return leaderboard.monthly;
      case 'allTime':
        return leaderboard.allTime;
      default:
        return leaderboard.daily;
    }
  }

  String _displayClientId(String clientId) {
    if (clientId.trim().isEmpty) return 'Oyuncu';
    return clientId;
  }

  @override
  Widget build(BuildContext context) {
    final selectedLeaderboard = _selectedLeaderboard;

    return Scaffold(
      backgroundColor: const Color(0xFF101B16),
      appBar: AppBar(
        title: const Text(
          'Liderlik Tablosu',
          style: TextStyle(
            fontWeight: FontWeight.bold,
          ),
        ),
        backgroundColor: const Color(0xFF17251E),
        foregroundColor: Colors.white,
        elevation: 0,
      ),
      body: SafeArea(
        child: _isLoading
            ? const Center(
          child: CircularProgressIndicator(
            color: Color(0xFFB7D7A8),
          ),
        )
            : _errorMessage != null
            ? _buildError()
            : selectedLeaderboard == null
            ? _buildEmpty()
            : RefreshIndicator(
          onRefresh: _loadLeaderboard,
          color: const Color(0xFFB7D7A8),
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              _buildHeader(),
              const SizedBox(height: 20),
              _buildCountrySelector(),
              const SizedBox(height: 16),
              _buildPeriodSelector(),
              const SizedBox(height: 24),
              _buildPodium(selectedLeaderboard),
              const SizedBox(height: 24),
              _buildRankingList(selectedLeaderboard),
              const SizedBox(height: 16),
              _buildCurrentUserCard(selectedLeaderboard),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildHeader() {
    return Column(
      children: [
        const Icon(
          Icons.emoji_events_rounded,
          size: 48,
          color: Color(0xFFD6B46A),
        ),
        const SizedBox(height: 8),
        const Text(
          'En İyi Oyuncular',
          style: TextStyle(
            fontSize: 23,
            fontWeight: FontWeight.bold,
            color: Colors.white,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          'En yüksek puana sahip oyuncular',
          style: TextStyle(
            fontSize: 13,
            color: Colors.white.withValues(alpha: 0.6),
          ),
        ),
      ],
    );
  }

  Widget _buildCountrySelector() {
    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(
        color: const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          _buildCountryButton(
            label: 'Global',
            value: 'Global',
          ),
          if (_detectedCountryCode != null &&
              _detectedCountryName != null)
            _buildCountryButton(
              label: _detectedCountryName!,
              value: _detectedCountryCode!,
            ),
        ],
      ),
    );
  }

  Widget _buildCountryButton({
    required String label,
    required String value,
  }) {
    final isSelected = _selectedCountry == value;

    return Expanded(
      child: GestureDetector(
        onTap: () {
          if (_selectedCountry == value) return;

          setState(() {
            _selectedCountry = value;
          });

          _loadLeaderboard();
        },
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: 8,
            vertical: 11,
          ),
          decoration: BoxDecoration(
            color: isSelected
                ? const Color(0xFF354A39)
                : Colors.transparent,
            borderRadius: BorderRadius.circular(9),
          ),
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: isSelected
                  ? const Color(0xFFB7D7A8)
                  : Colors.white70,
              fontWeight:
              isSelected ? FontWeight.bold : FontWeight.normal,
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildPeriodSelector() {
    final periods = [
      {'value': 'daily', 'label': 'Günlük'},
      {'value': 'weekly', 'label': 'Haftalık'},
      {'value': 'monthly', 'label': 'Aylık'},
      {'value': 'allTime', 'label': 'Tüm Zamanlar'},
    ];

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: periods.map((period) {
          final value = period['value']!;
          final label = period['label']!;
          final isSelected = _selectedPeriod == value;

          return Padding(
            padding: const EdgeInsets.only(right: 8),
            child: GestureDetector(
              onTap: () {
                if (_selectedPeriod == value) return;

                setState(() {
                  _selectedPeriod = value;
                });
              },
              child: Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 10,
                ),
                decoration: BoxDecoration(
                  color: isSelected
                      ? const Color(0xFFB7D7A8)
                      : const Color(0xFF1B2923),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Text(
                  label,
                  style: TextStyle(
                    color: isSelected
                        ? const Color(0xFF17251E)
                        : Colors.white70,
                    fontWeight: FontWeight.bold,
                    fontSize: 12,
                  ),
                ),
              ),
            ),
          );
        }).toList(),
      ),
    );
  }

  Widget _buildPodium(LeaderboardPeriod leaderboard) {
    final entries = leaderboard.rankings;

    if (entries.isEmpty) {
      return const SizedBox.shrink();
    }

    final first = entries.where((e) => e.rank == 1).firstOrNull;
    final second = entries.where((e) => e.rank == 2).firstOrNull;
    final third = entries.where((e) => e.rank == 3).firstOrNull;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Expanded(
          child: second == null
              ? const SizedBox.shrink()
              : _buildPodiumItem(second, 2),
        ),
        Expanded(
          child: first == null
              ? const SizedBox.shrink()
              : _buildPodiumItem(first, 1),
        ),
        Expanded(
          child: third == null
              ? const SizedBox.shrink()
              : _buildPodiumItem(third, 3),
        ),
      ],
    );
  }

  Widget _buildPodiumItem(
      LeaderboardEntry entry,
      int position,
      ) {
    final isFirst = position == 1;

    final currentUser = _selectedLeaderboard?.currentUser;
    final isCurrentUser =
        currentUser?.clientId == entry.clientId;

    final Color medalColor;

    switch (position) {
      case 1:
        medalColor = const Color(0xFFFFD700);
        break;
      case 2:
        medalColor = const Color(0xFFC0C0C0);
        break;
      default:
        medalColor = const Color(0xFFCD7F32);
    }

    final height = isFirst ? 150.0 : 125.0;

    return Column(
      children: [
        Icon(
          Icons.emoji_events_rounded,
          size: isFirst ? 38 : 30,
          color: medalColor,
        ),
        const SizedBox(height: 5),
        CircleAvatar(
          radius: isFirst ? 27 : 23,
          backgroundColor: medalColor,
          child: Icon(
            Icons.person,
            color: const Color(0xFF17251E),
            size: isFirst ? 30 : 25,
          ),
        ),
        const SizedBox(height: 7),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 3),
          child: Wrap(
            alignment: WrapAlignment.center,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 4,
            runSpacing: 3,
            children: [
              Text(
                _displayClientId(entry.clientId),
                softWrap: true,
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: isFirst ? 11 : 10,
                  fontWeight: FontWeight.bold,
                  color: isCurrentUser
                      ? const Color(0xFFD6B46A)
                      : Colors.white,
                ),
              ),
              if (isCurrentUser)
                const Text(
                  'SEN',
                  style: TextStyle(
                    fontSize: 9,
                    fontWeight: FontWeight.bold,
                    color: Color(0xFFD6B46A),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 4),
        Text(
          '${entry.solvedCount} bulmaca',
          textAlign: TextAlign.center,
          style: const TextStyle(
            fontSize: 10,
            color: Colors.white70,
          ),
        ),
        const SizedBox(height: 3),
        Text(
          '${entry.totalScore} puan',
          style: TextStyle(
            fontSize: isFirst ? 13 : 11,
            fontWeight: FontWeight.bold,
            color: const Color(0xFFB7D7A8),
          ),
        ),
        const SizedBox(height: 8),
        Container(
          height: height,
          decoration: BoxDecoration(
            color: isCurrentUser
                ? const Color(0xFF354A39)
                : const Color(0xFF1B2923),
            borderRadius: const BorderRadius.vertical(
              top: Radius.circular(12),
            ),
            border: Border.all(
              color: isCurrentUser
                  ? const Color(0xFFD6B46A)
                  : medalColor.withValues(alpha: 0.4),
              width: isCurrentUser ? 2 : 1,
            ),
          ),
          child: Center(
            child: Text(
              '#$position',
              style: TextStyle(
                fontSize: isFirst ? 26 : 21,
                fontWeight: FontWeight.bold,
                color: medalColor,
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildRankingList(LeaderboardPeriod leaderboard) {
    final entries = leaderboard.rankings
        .where((entry) => entry.rank > 3)
        .toList();

    if (entries.isEmpty) {
      return const SizedBox.shrink();
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Diğer Sıralamalar',
          style: TextStyle(
            fontSize: 17,
            fontWeight: FontWeight.bold,
            color: Colors.white,
          ),
        ),
        const SizedBox(height: 12),
        ...entries.map(_buildRankingTile),
      ],
    );
  }

  Widget _buildRankingTile(LeaderboardEntry entry) {
    final currentUser = _selectedLeaderboard?.currentUser;
    final isCurrentUser =
        currentUser?.clientId == entry.clientId;

    return Container(
      margin: const EdgeInsets.only(bottom: 9),
      padding: const EdgeInsets.symmetric(
        horizontal: 14,
        vertical: 14,
      ),
      decoration: BoxDecoration(
        color: isCurrentUser
            ? const Color(0xFF354A39)
            : const Color(0xFF1B2923),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: isCurrentUser
              ? const Color(0xFFD6B46A)
              : Colors.white10,
          width: isCurrentUser ? 2 : 1,
        ),
        boxShadow: isCurrentUser
            ? [
          BoxShadow(
            color: const Color(0xFFD6B46A)
                .withValues(alpha: 0.15),
            blurRadius: 12,
            spreadRadius: 1,
          ),
        ]
            : [],
      ),
      child: Row(
        children: [
          SizedBox(
            width: 34,
            child: Text(
              '#${entry.rank}',
              style: TextStyle(
                fontSize: isCurrentUser ? 16 : 14,
                fontWeight: FontWeight.bold,
                color: const Color(0xFFD6B46A),
              ),
            ),
          ),
          CircleAvatar(
            radius: 17,
            backgroundColor: isCurrentUser
                ? const Color(0xFFD6B46A)
                : const Color(0xFF304438),
            child: Icon(
              Icons.person,
              size: 19,
              color: isCurrentUser
                  ? const Color(0xFF192820)
                  : const Color(0xFFB7D7A8),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Wrap(
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 7,
                  runSpacing: 4,
                  children: [
                    Text(
                      _displayClientId(entry.clientId),
                      softWrap: true,
                      style: TextStyle(
                        fontWeight: FontWeight.bold,
                        color: isCurrentUser
                            ? const Color(0xFFD6B46A)
                            : Colors.white,
                      ),
                    ),
                    if (isCurrentUser)
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 7,
                          vertical: 3,
                        ),
                        decoration: BoxDecoration(
                          color: const Color(0xFFD6B46A),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: const Text(
                          'SEN',
                          style: TextStyle(
                            fontSize: 9,
                            fontWeight: FontWeight.bold,
                            color: Color(0xFF192820),
                          ),
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  '${entry.solvedCount} bulmaca çözdü',
                  style: const TextStyle(
                    fontSize: 11,
                    color: Colors.white70,
                  ),
                ),
              ],
            ),
          ),
          Text(
            '${entry.totalScore}',
            style: TextStyle(
              fontSize: isCurrentUser ? 18 : 16,
              fontWeight: FontWeight.bold,
              color: const Color(0xFFB7D7A8),
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

  Widget _buildCurrentUserCard(LeaderboardPeriod leaderboard) {
    final entry = leaderboard.currentUser;

    if (entry == null) {
      return const SizedBox.shrink();
    }

    final isAlreadyInList = leaderboard.rankings.any(
          (item) => item.clientId == entry.clientId,
    );

    if (isAlreadyInList) {
      return const SizedBox.shrink();
    }

    return Container(
      margin: const EdgeInsets.only(top: 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF354A39),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: const Color(0xFFD6B46A),
          width: 2,
        ),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFFD6B46A)
                .withValues(alpha: 0.15),
            blurRadius: 12,
            spreadRadius: 1,
          ),
        ],
      ),
      child: Row(
        children: [
          const Icon(
            Icons.person_pin_circle_rounded,
            color: Color(0xFFD6B46A),
            size: 28,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'SENİN SIRALAMAN',
                  style: TextStyle(
                    color: Color(0xFFD6B46A),
                    fontSize: 11,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 5),
                Text(
                  _displayClientId(entry.clientId),
                  softWrap: true,
                  style: const TextStyle(
                    color: Colors.white,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  '${entry.solvedCount} bulmaca çözdün',
                  style: const TextStyle(
                    color: Colors.white70,
                    fontSize: 11,
                  ),
                ),
              ],
            ),
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                '#${entry.rank}',
                style: const TextStyle(
                  color: Color(0xFFD6B46A),
                  fontSize: 20,
                  fontWeight: FontWeight.bold,
                ),
              ),
              Text(
                '${entry.totalScore} puan',
                style: const TextStyle(
                  color: Color(0xFFB7D7A8),
                  fontWeight: FontWeight.bold,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildError() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.error_outline,
              size: 48,
              color: Colors.redAccent,
            ),
            const SizedBox(height: 12),
            const Text(
              'Liderlik tablosu yüklenemedi.',
              textAlign: TextAlign.center,
              style: TextStyle(
                color: Colors.white,
                fontSize: 16,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              _errorMessage ?? '',
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white54,
                fontSize: 12,
              ),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: _loadLeaderboard,
              child: const Text('Tekrar Dene'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildEmpty() {
    return const Center(
      child: Text(
        'Henüz sıralama verisi bulunmuyor.',
        style: TextStyle(
          color: Colors.white70,
          fontSize: 15,
        ),
      ),
    );
  }
}
