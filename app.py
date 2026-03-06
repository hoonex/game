import streamlit as st
import numpy as np

# --- 1. 글로벌 상태 (모든 유저가 공유하는 저장소) ---
@st.cache_resource
def get_rooms():
    return {}

rooms = get_rooms()
BOARD_SIZE = 15

# CSS 스타일 적용
st.markdown("""
    <style>
    div[data-testid="column"] { padding: 0 !important; }
    button { height: 40px !important; width: 100% !important; padding: 0 !important; }
    </style>
""", unsafe_allow_html=True)

# --- 2. 내 로컬 세션 초기화 ---
if "room_code" not in st.session_state:
    st.session_state.room_code = None
if "my_role" not in st.session_state:
    st.session_state.my_role = None

# --- 3. 승리 판정 함수 ---
def check_win(board, player):
    for r in range(BOARD_SIZE):
        for c in range(BOARD_SIZE):
            if board[r, c] == player:
                if c <= BOARD_SIZE - 5 and all(board[r, c+i] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and all(board[r+i, c] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and c <= BOARD_SIZE - 5 and all(board[r+i, c+i] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and c >= 4 and all(board[r+i, c-i] == player for i in range(5)): return True
    return False

# --- 4. 대기실 (방 생성 및 입장) ---
if st.session_state.room_code is None:
    st.title("⚫⚪ 오목 대기실")
    room_input = st.text_input("방 코드를 입력하세요 (예: 1234)")
    
    if st.button("방 입장 / 생성"):
        if room_input:
            if room_input not in rooms:
                rooms[room_input] = {
                    "board": np.zeros((BOARD_SIZE, BOARD_SIZE), dtype=int),
                    "turn": 1,
                    "players": 1,
                    "game_over": False,
                    "winner": None
                }
                st.session_state.my_role = 1
                st.session_state.room_code = room_input
                st.rerun()
            elif rooms[room_input]["players"] == 1:
                rooms[room_input]["players"] = 2
                st.session_state.my_role = 2
                st.session_state.room_code = room_input
                st.rerun()
            else:
                st.error("이미 두 명이 꽉 찬 방입니다!")

# --- 5. 게임 화면 ---
else:
    code = st.session_state.room_code
    game = rooms[code]
    my_role = st.session_state.my_role
    
    role_text = "흑돌(⚫)" if my_role == 1 else "백돌(⚪)"
    st.title(f"오목 방: [{code}]")
    st.write(f"내 역할: **{role_text}**")
    
    # [부분 갱신 1] 상대방 기다리기 (0.5초마다 자동 갱신)
    if game["players"] < 2:
        @st.fragment(run_every=0.5)
        def wait_for_opponent():
            if game["players"] < 2:
                st.info("⏳ 상대방이 접속할 때까지 기다리는 중입니다... (자동 확인 중)")
            else:
                st.rerun() # 상대가 들어오면 전체 화면을 한 번 새로고침해서 게임 시작
        wait_for_opponent()

    # [부분 갱신 2] 본격적인 게임 보드 (0.5초마다 자동 갱신)
    else:
        def place_stone(r, c):
            if game["board"][r, c] == 0 and not game["game_over"] and game["turn"] == my_role:
                game["board"][r, c] = my_role
                if check_win(game["board"], my_role):
                    game["winner"] = "흑돌(⚫)" if my_role == 1 else "백돌(⚪)"
                    game["game_over"] = True
                else:
                    game["turn"] = 2 if my_role == 1 else 1

        # @st.fragment 떡인 이 함수 안쪽만 0.5초(run_every=0.5)마다 다시 그려집니다.
        @st.fragment(run_every=0.5)
        def render_board():
            # 1. 상태 표시
            if game["game_over"]:
                st.success(f"🎉 **{game['winner']} 승리!**")
                if st.button("대기실로 돌아가기"):
                    st.session_state.room_code = None
                    st.rerun()
            else:
                current_turn = "흑돌(⚫)" if game["turn"] == 1 else "백돌(⚪)"
                if game["turn"] != my_role:
                    st.warning(f"상대방({current_turn})의 턴입니다. 기다려주세요...")
                else:
                    st.info(f"내 턴입니다! 원하는 곳에 돌을 두세요.")

            # 2. 오목판 그리기
            for r in range(BOARD_SIZE):
                cols = st.columns(BOARD_SIZE)
                for c in range(BOARD_SIZE):
                    val = game["board"][r, c]
                    symbol = "⚫" if val == 1 else "⚪" if val == 2 else "ㅤ"
                    
                    # 내 턴이 아니거나, 겜이 끝났거나, 돌이 있으면 클릭 불가
                    is_disabled = (game["turn"] != my_role) or game["game_over"] or (val != 0)
                    
                    with cols[c]:
                        st.button(
                            symbol, 
                            key=f"btn_{r}_{c}", 
                            on_click=place_stone, 
                            args=(r, c),
                            disabled=is_disabled
                        )
        
        # 바둑판 렌더링 함수 실행
        render_board()
