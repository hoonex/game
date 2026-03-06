import streamlit as st
import numpy as np

# --- 1. 글로벌 상태 (공유 메모리) ---
@st.cache_resource
def get_rooms():
    return {}

rooms = get_rooms()
BOARD_SIZE = 15

# --- 🌟 2. 진짜 바둑판(교차점) CSS 스타일링 🌟 ---
st.markdown("""
    <style>
    /* 1. 바둑판 전체 테두리와 배경 (나무 질감) */
    div[data-testid="stHorizontalBlock"] {
        gap: 0rem !important; /* 가로 칸 사이의 여백 완전 제거! */
        background-color: #DCB35C; 
        padding: 15px; /* 바둑판 바깥쪽 여백 */
        border: 5px solid #4A330A; /* 두꺼운 나무 테두리 */
        box-shadow: 5px 5px 15px rgba(0,0,0,0.5); 
        max-width: 600px;
        margin: auto;
    }
    
    /* 2. 세로 줄 사이의 여백 제거 */
    div[data-testid="column"] { 
        padding: 0 !important; 
        min-width: 0 !important;
        gap: 0 !important;
    }

    /* 3. 버튼을 '선과 선이 만나는 교차점'으로 둔갑시키는 마법 */
    button {
        background-color: transparent !important;
        border: none !important; /* 기본 테두리 없앰 */
        border-radius: 0px !important;
        aspect-ratio: 1 / 1 !important; /* 완벽한 정사각형 유지 */
        height: auto !important;
        width: 100% !important;
        padding: 0 !important;
        margin: 0 !important;
        font-size: 28px !important; /* 바둑돌 크기 */
        display: flex;
        align-items: center;
        justify-content: center;
        
        /* 배경에 검은색 십자가(+) 선을 그립니다. 이게 바둑판의 선이 됩니다! */
        background-image: 
            linear-gradient(to right, transparent 48%, #111 48%, #111 52%, transparent 52%),
            linear-gradient(to bottom, transparent 48%, #111 48%, #111 52%, transparent 52%) !important;
    }

    /* 마우스 올렸을 때 교차점 중앙만 살짝 반응하게 */
    button:hover {
        background-color: rgba(0, 0, 0, 0.15) !important; 
    }

    /* 돌이 놓였을 때 이모지가 선(배경)을 덮으면서 그럴싸하게 보임 */
    button:disabled {
        color: black !important;
        opacity: 1 !important;
        cursor: default !important;
    }
    </style>
""", unsafe_allow_html=True)

# --- 3. 내 로컬 세션 초기화 ---
if "room_code" not in st.session_state:
    st.session_state.room_code = None
if "my_role" not in st.session_state:
    st.session_state.my_role = None

# --- 4. 승리 판정 함수 ---
def check_win(board, player):
    for r in range(BOARD_SIZE):
        for c in range(BOARD_SIZE):
            if board[r, c] == player:
                if c <= BOARD_SIZE - 5 and all(board[r, c+i] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and all(board[r+i, c] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and c <= BOARD_SIZE - 5 and all(board[r+i, c+i] == player for i in range(5)): return True
                if r <= BOARD_SIZE - 5 and c >= 4 and all(board[r+i, c-i] == player for i in range(5)): return True
    return False

# --- 5. 대기실 (방 생성 및 입장) ---
if st.session_state.room_code is None:
    st.title("⚫⚪ 진짜 바둑판 오목")
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

# --- 6. 게임 화면 (0.5초 자동 동기화) ---
else:
    code = st.session_state.room_code
    game = rooms[code]
    my_role = st.session_state.my_role
    
    role_text = "흑돌(⚫)" if my_role == 1 else "백돌(⚪)"
    st.markdown(f"### 🚪 방 제목: [{code}] | 👤 내 역할: **{role_text}**")
    
    # 상대방 기다리기
    if game["players"] < 2:
        @st.fragment(run_every=0.5)
        def wait_for_opponent():
            if game["players"] < 2:
                st.info("⏳ 상대방이 접속할 때까지 기다리는 중입니다...")
            else:
                st.rerun() 
        wait_for_opponent()

    # 본격적인 게임 보드
    else:
        def place_stone(r, c):
            if game["board"][r, c] == 0 and not game["game_over"] and game["turn"] == my_role:
                game["board"][r, c] = my_role
                if check_win(game["board"], my_role):
                    game["winner"] = "흑돌(⚫)" if my_role == 1 else "백돌(⚪)"
                    game["game_over"] = True
                else:
                    game["turn"] = 2 if my_role == 1 else 1

        @st.fragment(run_every=0.5)
        def render_board():
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
                    st.info(f"내 턴입니다! 교차점 중앙을 눌러 돌을 두세요.")

            st.write("") # 약간의 여백
            
            # 오목판 그리기
            for r in range(BOARD_SIZE):
                cols = st.columns(BOARD_SIZE)
                for c in range(BOARD_SIZE):
                    val = game["board"][r, c]
                    symbol = "⚫" if val == 1 else "⚪" if val == 2 else "ㅤ"
                    
                    is_disabled = (game["turn"] != my_role) or game["game_over"] or (val != 0)
                    
                    with cols[c]:
                        st.button(
                            symbol, 
                            key=f"btn_{r}_{c}", 
                            on_click=place_stone, 
                            args=(r, c),
                            disabled=is_disabled
                        )
        
        render_board()
