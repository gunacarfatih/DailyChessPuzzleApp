
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../core/services/location_service.dart';
import '../../../providers/leaderboard_provider.dart';
import '../models/leaderboard_entry.dart';
import '../models/leaderboard_period.dart';
import '../models/leaderboard_response.dart';

class LeaderboardPage extends StatefulWidget {
  const LeaderboardPage({super.key});

  @override
  State<LeaderboardPage> createState() => LeaderboardPageState();
}

class LeaderboardPageState extends State<LeaderboardPage> {
  final LocationService _locationService = LocationService();

  // Konum bilgisi belirlenene kadar ilk yükleme ekranını gösterir.
  bool _isInitializing = true;

  // Kullanıcının seçtiği dönem.
  String _selectedPeriod = 'daily';

  // Kullanıcının seçtiği ülke.
  String _selectedCountry = 'Global';

  // Cihazın tespit edilen ülke bilgileri.
  String? _detectedCountryCode;
  String? _detectedCountryName;

  @override
  void initState() {
    super.initState();
    _initializeLocationAndLoadLeaderboard();
  }

  // Önce kullanıcının ülkesini tespit eder,
  // ardından ilgili leaderboard verisini yükler.
  Future<void> _initializeLocationAndLoadLeaderboard() async {
    try {
      final countryInfo = await _locationService.getCountryInfo();

      if (!mounted) return;

      if (countryInfo != null) {
        setState(() {
          _detectedCountryCode = countryInfo.code;
          _detectedCountryName = countryInfo.name;
        });
      }

      await _loadLeaderboard();
    } catch (error) {
      if (kDebugMode) {
        debugPrint('Konum bilgisi yükleme hatası: $error');
      }

      if (mounted) {
        await _loadLeaderboard();
      }
    } finally {
      if (mounted) {
        setState(() {
          _isInitializing = false;
        });
      }
    }
  }

  // Seçili ülkenin leaderboard verisini Provider üzerinden yükler.
  //
  // forceRefresh false:
  // Bellekte veri varsa mevcut veriyi kullanır.
  //
  // forceRefresh true:
  // API üzerinden yeniden veri ister.
  Future<void> _loadLeaderboard({
    bool forceRefresh = false,
  }) async {
    if (!mounted) return;

    final country = _selectedCountry == 'Global'
        ? null
        : _detectedCountryCode;

    await context.read<LeaderboardProvider>().loadLeaderboard(
      country: country,
      period: _selectedPeriod.toLowerCase(),
      forceRefresh: forceRefresh,
    );
  }

  // MainNavigationPage ve SignalR tarafından çağrılır.
  // Mevcut leaderboard verisini zorla yeniler.
  Future<void> refreshLeaderboard() async {
    if (kDebugMode) {
      debugPrint(
        'LEADERBOARD PAGE: refreshLeaderboard() çağrıldı.',
      );
    }

    if (!mounted) return;

    final country = _selectedCountry == 'Global'
        ? null
        : _detectedCountryCode;

    await context.read<LeaderboardProvider>().refreshLeaderboard(
      country: country,
      period: 'all',
    );
  }

  // API yanıtından seçili dönemin verisini alır.
  LeaderboardPeriod? _getSelectedLeaderboard(
      LeaderboardResponse? leaderboard,
      ) {
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

  // Kullanıcıya gösterilecek Username bilgisini düzenler.
  String _displayUsername(String username) {
    if (username.trim().isEmpty) return 'Oyuncu';
    return username;
  }

  @override
  Widget build(BuildContext context) {
    // Provider değişikliklerini dinler.
    final leaderboardProvider =
    context.watch<LeaderboardProvider>();

    // Seçili ülkenin leaderboard anahtarı.
    final selectedCountryCode = _selectedCountry == 'Global'
        ? null
        : _detectedCountryCode;

    // Provider'dan seçili ülkenin verisini alır.
    final leaderboard = leaderboardProvider.leaderboardFor(
      selectedCountryCode,
      period: _selectedPeriod.toLowerCase(),
    );

    // Seçili dönemin verisini belirler.
    final selectedLeaderboard =
    _getSelectedLeaderboard(leaderboard);

    // Yüklenme ve hata durumlarını Provider'dan alır.
    final isLoading = _isInitializing ||
        leaderboardProvider.isLoadingFor(
          selectedCountryCode,
          period: _selectedPeriod.toLowerCase(),
        );

    final errorMessage = leaderboardProvider.errorFor(
      selectedCountryCode,
      period: _selectedPeriod.toLowerCase(),
    );

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
        child: isLoading
            ? const Center(
          child: CircularProgressIndicator(
            color: Color(0xFFB7D7A8),
          ),
        )
            : errorMessage != null
            ? _buildError(errorMessage)
            : selectedLeaderboard == null
            ? _buildEmpty()
            : RefreshIndicator(
          onRefresh: () => _loadLeaderboard(
            forceRefresh: true,
          ),
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

              // Sıralama ve kullanıcı verisi yoksa
              // bilgilendirme mesajı göster.
              if (selectedLeaderboard.rankings.isEmpty &&
                  selectedLeaderboard.currentUser == null)
                const Padding(
                  padding: EdgeInsets.symmetric(
                    vertical: 50,
                  ),
                  child: Center(
                    child: Text(
                      'Henüz sıralama verisi bulunmuyor.',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        color: Colors.white70,
                        fontSize: 15,
                      ),
                    ),
                  ),
                )
              else ...[
                // Sıralama listesi doluysa podyumu
                // ve diğer sıralamaları göster.
                if (selectedLeaderboard.rankings.isNotEmpty) ...[
                  _buildPodium(selectedLeaderboard),
                  const SizedBox(height: 24),
                  _buildRankingList(selectedLeaderboard),
                  const SizedBox(height: 16),
                ],

                // Kullanıcı ilk 10 içerisinde değilse
                // kendi sıralamasını ayrıca göster.
                if (selectedLeaderboard.currentUser != null)
                  _buildCurrentUserCard(selectedLeaderboard),
              ],
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

          // Provider daha önce yüklenmiş veriyi varsa kullanır.
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
                _loadLeaderboard();
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
              : _buildPodiumItem(
            second,
            2,
            leaderboard.currentUser,
          ),
        ),
        Expanded(
          child: first == null
              ? const SizedBox.shrink()
              : _buildPodiumItem(
            first,
            1,
            leaderboard.currentUser,
          ),
        ),
        Expanded(
          child: third == null
              ? const SizedBox.shrink()
              : _buildPodiumItem(
            third,
            3,
            leaderboard.currentUser,
          ),
        ),
      ],
    );
  }

  Widget _buildPodiumItem(
      LeaderboardEntry entry,
      int position,
      LeaderboardEntry? currentUser,
      ) {
    final isFirst = position == 1;

    final isCurrentUser =
        currentUser?.publicId == entry.publicId;

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
                _displayUsername(entry.username),
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
    // Podyumda gösterilecek her derece için bir temsilci seçilir.
    final podiumRepresentatives = <LeaderboardEntry>[];

    for (int rank = 1; rank <= 3; rank++) {
      final representative = leaderboard.rankings
          .where((entry) => entry.rank == rank)
          .firstOrNull;

      if (representative != null) {
        podiumRepresentatives.add(representative);
      }
    }

    // Podyum temsilcilerinin PublicId değerleri alınır.
    final podiumPublicIds = podiumRepresentatives
        .map((entry) => entry.publicId)
        .toSet();

    // Yalnızca podyum temsilcileri listeden çıkarılır.
    // Aynı dereceyi paylaşan diğer kullanıcılar listede kalır.
    final entries = leaderboard.rankings
        .where((entry) => !podiumPublicIds.contains(entry.publicId))
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
        ...entries.map(
              (entry) => _buildRankingTile(
            entry,
            leaderboard.currentUser,
          ),
        ),
      ],
    );
  }

  Widget _buildRankingTile(
      LeaderboardEntry entry,
      LeaderboardEntry? currentUser,
      ) {
    final isCurrentUser =
        currentUser?.publicId == entry.publicId;

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
                      _displayUsername(entry.username),
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

    // Kullanıcı zaten ilk 10 sıralama içerisinde bulunuyorsa
    // kişisel sıralama kartını tekrar gösterme.
    final isAlreadyInList = leaderboard.rankings.any(
          (item) => item.publicId == entry.publicId,
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
                  _displayUsername(entry.username),
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

  Widget _buildError(String errorMessage) {
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
              errorMessage,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white54,
                fontSize: 12,
              ),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: () => _loadLeaderboard(
                forceRefresh: true,
              ),
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
